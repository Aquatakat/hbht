using System.Globalization;
using Aidyn.Core.Actors;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// An actor's states and branches, with the controls to drive its state machine by a request.
/// <see cref="ModelView"/> does the playing; this only shows and asks.
/// </summary>
public partial class ActorPanel : VBoxContainer
{
    /// <summary>What the decomp names requests (<c>AniTypes</c>). The rest show as numbers.</summary>
    private static readonly Dictionary<int, string> RequestNames = new()
    {
        [0] = "idle", [2] = "walk", [3] = "run", [7] = "hit", [9] = "dying", [25] = "sneak",
        [ActorStateMachine.AnyRequest] = "any",
    };

    private static readonly Color Interrupting = new(1f, 0.75f, 0.4f);

    private readonly CheckBox _drive = new() { Text = "Drive", TooltipText = "Run the actor's state machine, asking for this request." };
    private readonly OptionButton _request = new() { TooltipText = "The request" };
    private readonly CheckBox _inPlace = new()
    {
        Text = "Root in place",
        TooltipText = "Draw the root transform with its position zeroed, as the game does. Driving always does, and moves the actor instead.",
    };
    private readonly Button _home = new() { Text = "⟲", TooltipText = "Put the actor back at the origin." };
    private readonly Label _clock = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Tree _states = new() { SizeFlagsVertical = SizeFlags.ExpandFill };
    private readonly List<TreeItem> _rows = [];
    private bool _selecting;

    public ActorPanel()
    {
        var bar = new HBoxContainer();
        bar.AddChild(_drive);
        bar.AddChild(_request);
        bar.AddChild(_home);
        bar.AddChild(_inPlace);
        bar.AddChild(_clock);
        AddChild(bar);

        _states.Columns = 4;
        _states.HideRoot = true;
        _states.ColumnTitlesVisible = true;
        _states.SelectMode = Tree.SelectModeEnum.Row;
        string[] titles = ["State", "Animation", "Ticks", "Branches (request, chance → state; orange interrupts)"];
        for (var c = 0; c < titles.Length; c++)
            _states.SetColumnTitle(c, titles[c]);
        for (var c = 0; c < 3; c++)
        {
            _states.SetColumnExpand(c, false);
            _states.SetColumnCustomMinimumWidth(c, c == 1 ? 110 : c == 0 ? 90 : 60);
        }
        _states.SetColumnClipContent(3, true);
        _states.ItemSelected += () =>
        {
            if (!_selecting && _states.GetSelected() is { } row)
                StateClicked?.Invoke(row.GetMetadata(0).AsInt32());
        };
        AddChild(_states);

        _drive.Toggled += on => DriveChanged?.Invoke(on);
        _request.ItemSelected += _ => RequestChanged?.Invoke(Request);
        _inPlace.Toggled += on => InPlaceChanged?.Invoke(on);
        _home.Pressed += () => HomePressed?.Invoke();
    }

    public event Action<int>? StateClicked;
    public event Action<bool>? DriveChanged;
    public event Action<int>? RequestChanged;
    public event Action<bool>? InPlaceChanged;
    public event Action? HomePressed;

    public int Request => _request.GetSelectedId();

    /// <summary>Turns driving on with a request, as if picked.</summary>
    public void Drive(int request)
    {
        if (_request.GetItemIndex(request) is >= 0 and var i)
            _request.Select(i);
        _drive.ButtonPressed = true;
    }
    public bool InPlace => _inPlace.ButtonPressed;

    public void Show(ActorRecord actor, Func<int, int> length)
    {
        _drive.SetPressedNoSignal(false);
        _states.Clear();
        _rows.Clear();
        var root = _states.CreateItem();
        for (var n = 0; n < actor.States.Count; n++)
        {
            var state = actor.States[n];
            var row = _states.CreateItem(root);
            row.SetText(0, n == actor.InitialState ? $"{n} initial" : n.ToString(CultureInfo.InvariantCulture));
            row.SetText(1, state.Animation.Key);
            row.SetTooltipText(1, state.Animation.ToString());
            row.SetText(2, length(n).ToString(CultureInfo.InvariantCulture));
            row.SetText(3, string.Join(", ", state.Branches.Select(Branch)));
            row.SetTooltipText(3, string.Join("\n", state.Branches.Select(b => Branch(b) + (b.Interrupts ? " (interrupts)" : ""))));
            if (state.Branches.Any(b => b.Interrupts))
                row.SetCustomColor(3, Interrupting);
            row.SetMetadata(0, n);
            _rows.Add(row);
        }

        // Idle, and every request a branch answers.
        _request.Clear();
        foreach (var r in actor.States.SelectMany(s => s.Branches).Select(b => b.Request).Where(r => r != ActorStateMachine.AnyRequest)
                     .Append(0).Distinct().Order())
            _request.AddItem(RequestName(r), r);
        _request.Select(0);
        _clock.Text = "";
    }

    /// <summary>Highlights a state, and shows the clock.</summary>
    public void Mark(int state, string clock)
    {
        _clock.Text = clock;
        if (state < 0 || state >= _rows.Count || _states.GetSelected() == _rows[state])
            return;
        _selecting = true;
        _rows[state].Select(0);
        _states.ScrollToItem(_rows[state]);
        _selecting = false;
    }

    private static string Branch(ActorBranch b) =>
        $"{RequestName(b.Request)}{(b.Chance < 100 ? $" {b.Chance}%" : "")} → {b.State}";

    private static string RequestName(int request) =>
        RequestNames.TryGetValue(request, out var name)
            ? request == ActorStateMachine.AnyRequest ? name : $"{request} {name}"
            : request.ToString(CultureInfo.InvariantCulture);
}
