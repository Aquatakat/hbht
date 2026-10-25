using System.Globalization;
using HBHT.Core.Controls;

namespace HBHT.Core.Ui;

/// <summary>A control waiting for an input to bind (<see cref="OptionsMenu.Prompt"/>).</summary>
/// <param name="Control">The control's ID.</param>
/// <param name="Device">The device whose input it takes.</param>
/// <param name="TicksLeft">The ticks until it gives up, changing nothing.</param>
public sealed record ControlPrompt(string Control, ControlDevice Device, int TicksLeft);

public sealed partial class OptionsMenu
{
    /// <summary>How long the prompt waits for an input before it gives up: 5 seconds. Nothing else ends it, so any key can be bound.</summary>
    public const int PromptTicks = 5 * 60;

    /// <summary>How long a message about a change shows: 3 seconds.</summary>
    public const int MessageTicks = 3 * 60;

    /// <summary>The mouse's sensitivities, as multiples of the drag the runtime always had.</summary>
    private static readonly float[] Sensitivities = [0.25f, 0.5f, 0.75f, 1, 1.5f, 2, 3, 4];

    private int _messageTicks;

    /// <summary>Which device's bindings the Controls page shows and changes, by its Device row.</summary>
    public ControlDevice Device { get; private set; }

    /// <summary>The control waiting for an input to bind, or null. While it waits, the page takes nothing but what it binds.</summary>
    public ControlPrompt? Prompt { get; private set; }

    /// <summary>What the last change came to, for a while: what lost a binding to it, or why it was refused.</summary>
    public string? Message { get; private set; }

    /// <summary>The prompt's text, in the cell it binds: "Press a Key... 5".</summary>
    public string? PromptText => Prompt is { } prompt
        ? $"{(prompt.Device == ControlDevice.Keyboard ? "Press a Key..." : "Press a Button...")} {(prompt.TicksLeft + 59) / 60}"
        : null;

    /// <summary>What clears a binding on the device shown, for the page's foot: the menus' Appraise.</summary>
    public string? ClearHint => Settings.Controls.Get(GameControls.MenuAppraise, Device) is { Count: > 0 } keys
        ? $"{Binding.Describe(keys)}: Clear"
        : null;

    private ControlBindings Bindings => Settings.Controls;

    /// <summary>
    /// The Controls page: the device first, then each situation's controls under its heading, each a binding or, for a direction, a
    /// choice among its sources' combinations, then the mouse's sensitivity on the keyboard's page, and Reset to Defaults.
    /// </summary>
    private List<OptionRow> ControlRows()
    {
        List<OptionRow> rows =
        [
            new("Device", OptionKind.Choice, () => (int)Device, v =>
            {
                Device = (ControlDevice)v;
                Rows.Rows = ControlRows();
            }, ["Keyboard", "Controller"]),
        ];
        var heading = Situation.None;
        foreach (var control in GameControls.All)
        {
            var title = control.Heading != heading ? GameControls.HeadingLabel(control.Heading) : null;
            heading = control.Heading;
            rows.Add(control.Kind == ControlKind.Sources ? SourcesRow(control, title) : BindingRow(control, title));
        }
        if (Device == ControlDevice.Keyboard)
        {
            rows.Add(new("Mouse Sensitivity", OptionKind.Choice, () => NearestIndex(Sensitivities, Settings.MouseSensitivity),
                v => Settings.MouseSensitivity = Sensitivities[v], [.. Sensitivities.Select(s => s.ToString("0.##", CultureInfo.InvariantCulture) + "x")], GapBefore: true));
        }
        rows.Add(new("Reset to Defaults", OptionKind.Command, () => 0, _ =>
        {
            Bindings.Reset();
            Say("Controls Reset");
        }, [], GapBefore: Device != ControlDevice.Keyboard));
        return rows;
    }

    private OptionRow BindingRow(GameControl control, string? heading) =>
        new(control.Label, OptionKind.Binding, () => 0, _ => { }, [], Shown: () => Binding.Describe(Bindings.Get(control.Id, Device)),
            Control: control.Id, Heading: heading);

    /// <summary>A direction's row: its sources' combinations on the device, none first, but for an essential control, which needs one.</summary>
    private OptionRow SourcesRow(GameControl control, string? heading)
    {
        var device = Device;
        var allowed = control.AllowedOn(device).ToList();
        var sets = Enumerable.Range(0, 1 << allowed.Count)
            .Select(mask => allowed.Where((_, i) => (mask & (1 << i)) != 0).ToList())
            .Where(set => set.Count > 0 || !control.Essential)
            .OrderBy(set => set.Count)
            .ToList();
        return new(control.Label, OptionKind.Choice,
            () => Math.Max(0, sets.FindIndex(set => set.SequenceEqual(Bindings.Get(control.Id, device)))),
            v => Report(Bindings.SetSources(control.Id, device, sets[v])),
            [.. sets.Select(Binding.Describe)], Control: control.Id, Heading: heading);
    }

    /// <summary>A on the Controls page: a binding's row starts the prompt, and Reset to Defaults resets. Whether it was one of them.</summary>
    private bool AcceptOnControls()
    {
        switch (Rows.Current)
        {
            case { Kind: OptionKind.Binding, Control: { } control }:
                Prompt = new ControlPrompt(control, Device, PromptTicks);
                Message = null;
                return true;
            case { Kind: OptionKind.Command } command:
                command.Set(0);
                Changed = true;
                Settings.Notify();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// An input while the prompt waits, as a binding (<see cref="Binding"/>): one of the prompt's device binds its control, in place of
    /// what it had there. Any other, or a debug key, is passed over and the prompt waits on. Whether it was taken.
    /// </summary>
    public bool Bind(string binding)
    {
        if (Prompt is not { } prompt || Binding.DeviceOf(binding) != prompt.Device || Binding.Reserved.Contains(binding))
            return false;
        Prompt = null;
        Report(Bindings.Rebind(prompt.Control, prompt.Device, binding));
        return true;
    }

    /// <summary>The menus' Appraise on a binding's row: clears it on the device shown, unless it's essential. Whether it was one.</summary>
    public bool Clear()
    {
        if (Prompt is not null || Rows.Current is not { Kind: OptionKind.Binding, Control: { } control })
            return false;
        Report(Bindings.Clear(control, Device));
        return true;
    }

    /// <summary>Says what a change came to, and keeps the settings if it was done.</summary>
    private void Report(BindResult result)
    {
        Say(result.Refusal ?? (result.TakenFrom.Count > 0 ? "Taken From " + string.Join(", ", result.TakenFrom) : null));
        if (!result.Done)
            return;
        Changed = true;
        Settings.Notify();
    }

    private void Say(string? message)
    {
        Message = message;
        _messageTicks = message is null ? 0 : MessageTicks;
    }

    private void StepPrompt()
    {
        if (Prompt is { } prompt)
            Prompt = prompt.TicksLeft > 1 ? prompt with { TicksLeft = prompt.TicksLeft - 1 } : null;
        if (_messageTicks > 0 && --_messageTicks == 0)
            Message = null;
    }
}
