using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aidyn.Core.Controls;

/// <summary>What a change to the bindings came to: done, or refused and why, and what lost a binding to it.</summary>
/// <param name="TakenFrom">The labels of the controls in the same situations that lost the input to it.</param>
/// <param name="Refusal">Why it was refused, in Title Case for the page, or null if it was done.</param>
public readonly record struct BindResult(IReadOnlyList<string> TakenFrom, string? Refusal = null)
{
    public bool Done => Refusal is null;

    public static BindResult Refused(string why) => new([], why);
}

/// <summary>Two controls that share an input in a situation they're both live in.</summary>
public readonly record struct BindingConflict(string First, string Second, string Input, Situation Situation);

/// <summary>
/// The player's bindings: each control's (<see cref="GameControls"/>) on the keyboard and mouse and on a controller, kept in
/// <see cref="Settings.Controls"/> and turned into Godot's input actions by the runtime. A new one holds the defaults, which are
/// the bindings the runtime always had.
/// </summary>
/// <remarks>
/// <para>
/// A button's cell is replaced whole by a rebind, so the page never adds a second key; the defaults' second keys (Escape beside
/// Enter) come back with Reset. The newest binding wins: a control in the same situations that had the input loses it, a button
/// its binding and a set of sources the source with it.
/// </para>
/// <para>
/// So the player can't lock themselves out, the essential controls (<see cref="GameControl.Essential"/>) always keep a binding on
/// each device: a change that would leave one with none is refused, and a file that leaves one with none gets its default back.
/// Two sets of sources may share a source, as Move and Camera share the left stick for <see cref="GameControls.CameraHold"/>.
/// </para>
/// </remarks>
[JsonConverter(typeof(ControlBindingsJson))]
public sealed class ControlBindings
{
    private readonly Dictionary<string, List<string>> _bindings = [];

    public ControlBindings() => Reset();

    /// <summary>Counts the changes, so the runtime rebuilds its input actions only when they change.</summary>
    public int Version { get; private set; }

    /// <summary>A control's bindings on both devices.</summary>
    public IReadOnlyList<string> Get(string control) => _bindings[control];

    /// <summary>A control's bindings on a device.</summary>
    public IReadOnlyList<string> Get(string control, ControlDevice device) => [.. _bindings[control].Where(b => Binding.DeviceOf(b) == device)];

    /// <summary>Whether every control has its default bindings.</summary>
    public bool IsDefault => GameControls.All.All(c => _bindings[c.Id].SequenceEqual([.. c.Keyboard, .. c.Controller]));

    /// <summary>Every control back to its defaults.</summary>
    public void Reset()
    {
        foreach (var control in GameControls.All)
            _bindings[control.Id] = [.. control.Keyboard, .. control.Controller];
        Version++;
    }

    /// <summary>A copy, changed apart from this one.</summary>
    public ControlBindings Copy()
    {
        var copy = new ControlBindings();
        foreach (var (id, list) in _bindings)
            copy._bindings[id] = [.. list];
        return copy;
    }

    /// <summary>
    /// Binds a button on a device to one input, in place of what it had there. Whatever else in its situations had the input loses it,
    /// unless that would leave an essential control with nothing on the device, which refuses the change. A source, an input of the
    /// other device and the debug keys' F-keys are refused.
    /// </summary>
    public BindResult Rebind(string id, ControlDevice device, string binding)
    {
        var control = GameControls.Get(id);
        if (control.Kind != ControlKind.Button || Binding.IsSource(binding) || Binding.DeviceOf(binding) != device)
            return BindResult.Refused(device == ControlDevice.Keyboard ? "Not a Key" : "Not a Button");
        if (Binding.Reserved.Contains(binding))
            return BindResult.Refused("Kept for Debugging");
        return Change(control, device, [binding]);
    }

    /// <summary>
    /// Sets the sources a direction control takes on a device. Buttons in its situations that had one of their inputs lose it; an
    /// essential control can't be left with none.
    /// </summary>
    public BindResult SetSources(string id, ControlDevice device, IEnumerable<string> sources)
    {
        var control = GameControls.Get(id);
        var list = sources.Distinct().ToList();
        if (control.Kind != ControlKind.Sources || list.Any(s => !control.AllowedOn(device).Contains(s)))
            return BindResult.Refused("Not a Source");
        // In the order the control lists them, whatever order they came in.
        list = [.. control.AllowedOn(device).Where(list.Contains)];
        return Change(control, device, list);
    }

    /// <summary>Leaves a control with nothing on a device, unless it's essential.</summary>
    public BindResult Clear(string id, ControlDevice device) => Change(GameControls.Get(id), device, []);

    /// <summary>Every pair of controls that share an input in a situation they're both live in. The defaults have none.</summary>
    public IEnumerable<BindingConflict> Conflicts()
    {
        var all = GameControls.All;
        for (var i = 0; i < all.Count; i++)
        {
            for (var j = i + 1; j < all.Count; j++)
            {
                var (a, b) = (all[i], all[j]);
                var shared = a.LiveIn & b.LiveIn;
                if (shared == Situation.None || a.Kind == ControlKind.Sources && b.Kind == ControlKind.Sources)
                    continue;
                var inputs = _bindings[b.Id].SelectMany(Binding.Inputs).ToHashSet();
                foreach (var input in _bindings[a.Id].SelectMany(Binding.Inputs).Where(inputs.Contains))
                    yield return new BindingConflict(a.Id, b.Id, input, shared);
            }
        }
    }

    /// <summary>The controls whose bindings aren't their defaults, with all their bindings: what <c>settings.json</c> keeps.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Changed() =>
        GameControls.All.Where(c => !_bindings[c.Id].SequenceEqual([.. c.Keyboard, .. c.Controller]))
            .ToDictionary(c => c.Id, c => (IReadOnlyList<string>)_bindings[c.Id]);

    /// <summary>
    /// Sets a control's bindings from a file, over its defaults: unknown controls and bindings that aren't one of the forms (or are
    /// sources a control can't take, or the debug keys) are skipped, and an essential control left with nothing on a device gets
    /// its default there back.
    /// </summary>
    public void Load(string id, IEnumerable<string> bindings)
    {
        if (GameControls.Find(id) is not { } control)
            return;
        var list = bindings.Where(b => Binding.IsValid(b) && !Binding.Reserved.Contains(b)
            && (control.Kind == ControlKind.Sources ? control.Allowed!.Contains(b) : !Binding.IsSource(b))).Distinct().ToList();
        foreach (var device in Enum.GetValues<ControlDevice>())
        {
            if (control.Essential && !list.Any(b => Binding.DeviceOf(b) == device))
                list.AddRange(control.Defaults(device));
        }
        _bindings[id] = list;
        Version++;
    }

    /// <summary>
    /// Replaces a control's cell on a device, taking each of its inputs from the other controls in its situations that had it: a
    /// button loses the binding, a set of sources the source.
    /// </summary>
    private BindResult Change(GameControl control, ControlDevice device, List<string> cell)
    {
        if (control.Essential && cell.Count == 0)
            return BindResult.Refused($"{control.Label} Needs a Binding");
        var inputs = cell.SelectMany(Binding.Inputs).ToHashSet();
        var losses = new Dictionary<string, List<string>>();
        foreach (var other in GameControls.All)
        {
            if (other == control || (other.LiveIn & control.LiveIn) == Situation.None
                || other.Kind == ControlKind.Sources && control.Kind == ControlKind.Sources)
                continue;
            var kept = _bindings[other.Id].Where(b => !Binding.Inputs(b).Any(inputs.Contains)).ToList();
            if (kept.Count == _bindings[other.Id].Count)
                continue;
            if (other.Essential && !kept.Any(b => Binding.DeviceOf(b) == device))
                return BindResult.Refused($"{other.Label} Needs It");
            losses[other.Id] = kept;
        }
        foreach (var (id, kept) in losses)
            _bindings[id] = kept;
        _bindings[control.Id] = [.. _bindings[control.Id].Where(b => Binding.DeviceOf(b) != device), .. cell];
        // Keyboard first, as the defaults are.
        _bindings[control.Id] = [.. _bindings[control.Id].OrderBy(b => Binding.DeviceOf(b))];
        Version++;
        return new BindResult([.. losses.Keys.Select(id => GameControls.Get(id).Label)]);
    }
}

/// <summary>
/// <c>settings.json</c>'s <c>controls</c>: the controls the player changed, each with all its bindings, so a control added later
/// takes its default.
/// </summary>
public sealed class ControlBindingsJson : JsonConverter<ControlBindings>
{
    public override ControlBindings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var bindings = new ControlBindings();
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return bindings;
        }
        var changed = JsonSerializer.Deserialize<Dictionary<string, string[]?>>(ref reader) ?? [];
        foreach (var (id, list) in changed)
            bindings.Load(id, list ?? []);
        return bindings;
    }

    public override void Write(Utf8JsonWriter writer, ControlBindings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (id, list) in value.Changed())
        {
            writer.WritePropertyName(id);
            writer.WriteStartArray();
            foreach (var binding in list)
                writer.WriteStringValue(binding);
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}
