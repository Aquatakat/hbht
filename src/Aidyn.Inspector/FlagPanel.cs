using Aidyn.Core.Flags;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The flags' values the editor shows: a new game's, or a checkpoint's plain flags with the derived
/// flags the port evaluates from them, which it checks against the console's.
/// </summary>
public sealed class FlagState(FlagNetwork network)
{
    public FlagNetwork Network { get; } = network;

    /// <summary>What the values are, such as "a new game" or a checkpoint's label.</summary>
    public string Name { get; private set; } = "a new game";

    /// <summary>After loading a checkpoint, how the port's derived flags compare with the console's.</summary>
    public string? Check { get; private set; }

    /// <summary>The console's values, from the loaded checkpoint.</summary>
    public IReadOnlyList<bool>? Console { get; private set; }

    /// <summary>The loaded checkpoint's party sheets, which character pages and the script player use.</summary>
    public IReadOnlyList<CheckpointCharacter>? ConsoleParty { get; private set; }

    /// <summary>The loaded checkpoint's gold.</summary>
    public uint? ConsoleGold { get; private set; }

    public void NewGame()
    {
        Network.NewGame();
        (Name, Check, Console, ConsoleParty, ConsoleGold) = ("a new game", null, null, null, null);
    }

    /// <summary>
    /// Restores a checkpoint's plain flags (an ares state, see docs/reference-rig.md). Base-game
    /// flags are numbered as the console's, and other plugins' flags start clear.
    /// </summary>
    public void LoadCheckpoint(string path)
    {
        var checkpoint = AresCheckpoint.Read(path);
        var console = checkpoint.Flags;
        try
        {
            (ConsoleParty, ConsoleGold) = (checkpoint.Party, checkpoint.Gold);
        }
        catch (InvalidDataException)
        {
            (ConsoleParty, ConsoleGold) = (null, null);
        }
        Network.Restore(h => h < console.Count && console[h]);
        var derived = Enumerable.Range(0, Math.Min(console.Count, Network.Count)).Where(Network.IsDerived).ToList();
        var wrong = derived.Where(h => Network.Peek(h) != console[h]).ToList();
        var file = Path.GetFileNameWithoutExtension(path);
        Name = file.Contains(" - ", StringComparison.Ordinal) ? file[(file.LastIndexOf(" - ", StringComparison.Ordinal) + 3)..] : file;
        Console = console;
        Check = wrong.Count == 0
            ? $"The network reproduces all {derived.Count:N0} of the checkpoint's derived flags. {console.Count(v => v):N0} flags are set."
            : $"{wrong.Count:N0} of {derived.Count:N0} derived flags differ from the console's: {string.Join(", ", wrong.Take(20))}{(wrong.Count > 20 ? "…" : "")}";
    }

    /// <summary>After a flag is set or cleared by hand.</summary>
    public void Edited()
    {
        if (!Name.EndsWith(", edited", StringComparison.Ordinal))
            Name += ", edited";
    }
}

/// <summary>
/// A flag's page: what it means, its journal entry, a derived flag's formula with each input's value,
/// and its own value under the <see cref="FlagState"/>. A journal entry's page shows its text and
/// its flags. Plain flags can be set and cleared, to see what follows.
/// </summary>
public partial class FlagPanel : VBoxContainer
{
    private static readonly Color Dim = new(0.65f, 0.65f, 0.65f);

    private readonly PluginCatalog _catalog;
    private readonly FlagState _state;
    private readonly RecordId _id;
    private readonly Action<RecordId> _open;
    private readonly VBoxContainer _body = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly FileDialog _dialog = new()
    {
        FileMode = FileDialog.FileModeEnum.OpenFile,
        Access = FileDialog.AccessEnum.Filesystem,
        Filters = ["*.bs1, *.bs2, *.bs3, *.bs4, *.bs5, *.bs6, *.bs7, *.bs8, *.bs9 ; ares save states"],
        UseNativeDialog = true,
        Title = "A checkpoint: an ares save state of 1.0 or the debug ROM",
    };

    public FlagPanel(PluginCatalog catalog, FlagState state, RecordId id, Action<RecordId> open)
    {
        (_catalog, _state, _id, _open) = (catalog, state, id, open);
        AddThemeConstantOverride("separation", 8);

        var bar = new HBoxContainer();
        var load = new Button { Text = "Load checkpoint…", TooltipText = "Show a checkpoint's flags, and check the network against them." };
        var newGame = new Button { Text = "New game", TooltipText = "Show a new game's flags." };
        bar.AddChild(load);
        bar.AddChild(newGame);
        AddChild(bar);
        AddChild(_body);
        AddChild(_dialog);

        load.Pressed += () => _dialog.PopupCentered(new Vector2I(800, 500));
        _dialog.FileSelected += path =>
        {
            try
            {
                _state.LoadCheckpoint(path);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                GD.PrintErr(e.ToString());
            }
            Refresh();
        };
        newGame.Pressed += () =>
        {
            _state.NewGame();
            Refresh();
        };
        Refresh();
    }

    private void Refresh()
    {
        foreach (var child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }

        Line($"Values: {_state.Name}.", Dim);
        if (_state.Check is { } check)
            Line(check, Dim);
        if (_id.Type == RecordTypes.Journal)
            ShowJournal();
        else
            ShowFlag();
    }

    private void ShowFlag()
    {
        var network = _state.Network;
        if (!network.TryHandle(_id, out var handle) || _catalog.Plugins.TryGet<FlagRecord>(_id) is not { } flag)
        {
            Line("No plugin defines this flag.");
            return;
        }

        var value = network.Get(handle);
        var heading = $"{(value ? "Set" : "Clear")}{ConsoleNote(handle)}" + (handle is FlagNetwork.False or FlagNetwork.True ? ", always" : "");
        if (!network.IsDerived(handle) && handle > FlagNetwork.True)
        {
            var toggle = new CheckBox { Text = heading, ButtonPressed = value, TooltipText = "Set or clear it, and see what follows." };
            toggle.Toggled += on =>
            {
                network.Set(handle, on);
                _state.Edited();
                Refresh();
            };
            _body.AddChild(toggle);
        }
        else
        {
            Line(heading);
        }
        if (flag.Label is { } label)
            Line(label);

        if (flag.Formula is not { } formula)
        {
            Line($"Plain: scripts, map objects or the engine set it. A new game starts with it {(flag.Initial ? "set" : "clear")}. The Atlas tab lists what sets and reads it.", Dim);
        }
        else
        {
            var bits = 0;
            for (var i = 0; i < formula.Inputs.Count; i++)
                bits |= network.Get(formula.Inputs[i]) ? 1 << i : 0;
            var count = formula.Inputs.Count(network.Get);
            var now = formula.Kind switch
            {
                FlagFormulaKind.Count => $"{count} set",
                _ when formula.Op == FlagOperation.Mask || formula.Kind == FlagFormulaKind.Value =>
                    $"{bits} (0b{Convert.ToString(bits, 2).PadLeft(formula.Inputs.Count, '0')})",
                _ => $"{count} of {formula.Inputs.Count} set",
            };
            Line($"Derived: {formula.Describe()}. Now {now}. Inputs, the first the lowest bit:");
            _body.AddChild(Inputs(formula.Inputs));
        }

        if (flag.Journal is { } journal)
        {
            Line("Turning on adds this journal entry, and raises the new-entry flag:");
            Link(journal, _catalog.Plugins.TryGet<JournalRecord>(journal)?.Text ?? "(no such entry)");
        }
    }

    private void ShowJournal()
    {
        if (_catalog.Plugins.TryGet<JournalRecord>(_id) is not { } entry)
        {
            Line("No plugin defines this journal entry.");
            return;
        }
        Line(entry.Text);
        Line("The journal lists it while this flag is set:", Dim);
        _body.AddChild(Inputs([entry.Flag]));
        var adders = _catalog.ReferencesTo(_id).Select(r => r.From).Where(f => f.Type == RecordTypes.Flag).Distinct().ToList();
        if (adders.Count > 0)
        {
            Line("It's added when this flag turns on:", Dim);
            _body.AddChild(Inputs(adders));
        }
    }

    /// <summary>Flags with their values, each opening its page on a double-click.</summary>
    private Tree Inputs(IReadOnlyList<RecordId> flags)
    {
        var tree = new Tree
        {
            Columns = 3,
            HideRoot = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(0, Math.Min(flags.Count, 12) * 26 + 8),
            ScrollHorizontalEnabled = false,
        };
        tree.SetColumnExpand(0, false);
        tree.SetColumnCustomMinimumWidth(0, 80);
        tree.SetColumnExpand(1, false);
        tree.SetColumnCustomMinimumWidth(1, 150);
        tree.SetColumnClipContent(2, true);
        var root = tree.CreateItem();
        foreach (var input in flags)
        {
            var row = tree.CreateItem(root);
            var known = _state.Network.TryHandle(input, out var handle);
            row.SetText(0, known ? (_state.Network.Get(handle) ? "set" : "clear") + ConsoleNote(handle) : "?");
            row.SetText(1, input.ToString());
            row.SetMetadata(1, input.ToString());
            row.SetText(2, _catalog.Find(input)?.Summary ?? "");
            row.SetTooltipText(2, _catalog.Find(input)?.Summary ?? "");
        }
        tree.ItemActivated += () =>
        {
            if (tree.GetSelected()?.GetMetadata(1).AsString() is { } text && RecordId.TryParse(text, out var id))
                _open(id);
        };
        return tree;
    }

    /// <summary>A note when the loaded checkpoint's console value differs.</summary>
    private string ConsoleNote(int handle) =>
        _state.Console is { } console && handle < console.Count && console[handle] != _state.Network.Peek(handle)
            ? $" (the console has it {(console[handle] ? "set" : "clear")})"
            : "";

    private void Line(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        _body.AddChild(label);
    }

    private void Link(RecordId id, string text)
    {
        var button = new Button
        {
            Text = $"{id}: {text}",
            TooltipText = "Open it.",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        button.Pressed += () => _open(id);
        _body.AddChild(button);
    }
}
