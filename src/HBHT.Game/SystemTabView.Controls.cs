using HBHT.Core.Ui;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

public partial class SystemTabView
{
    /// <summary>
    /// The Controls page: the Device row at the options' first row, then the list under it, scrolling, its lines 12 apart (a
    /// situation's heading takes a line), and the foot's line under the list. Values start at x 188, clear of the longest label, and run to 304, clear of the
    /// System tab's right edge; one longer is printed in the slim font, as a save's long name is.
    /// </summary>
    private const int ControlsTop = 70, ControlsListTop = 88, ControlsLines = 9, ControlsPitch = 12, ControlsValue = 188, ControlsRight = 304,
        ControlsFoot = 202;

    /// <summary>
    /// Where the Controls page's rows and headings are: each row's top and bottom, off the page for one scrolled out of view, and
    /// each heading in view with its top. The list scrolls to keep the cursor's row in the middle, as far as it can.
    /// </summary>
    private (List<(int Top, int Bottom)> Rows, List<(string Text, int Top)> Headings, bool Above, bool Below) ControlPlaces(OptionsMenu options)
    {
        var rows = options.Rows.Rows;
        // The list's lines: a heading's (row −1) and each row's, after the Device row.
        var lines = new List<(int Row, string? Heading)>();
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].Heading is { } heading)
                lines.Add((-1, heading));
            if (rows[i].GapBefore)
                lines.Add((-1, null));
            lines.Add((i, null));
        }
        var cursor = lines.FindIndex(l => l.Row == options.Rows.Cursor);
        var first = cursor < 0 ? 0 : Math.Clamp(cursor - ControlsLines / 2, 0, Math.Max(0, lines.Count - ControlsLines));
        var places = Enumerable.Repeat((-1000, -1000), rows.Count).ToList();
        places[0] = (ControlsTop, ControlsTop + _ui.Main.LineHeight);
        var headings = new List<(string, int)>();
        for (var l = first; l < Math.Min(lines.Count, first + ControlsLines); l++)
        {
            var top = ControlsListTop + (l - first) * ControlsPitch;
            if (lines[l].Row >= 0)
                places[lines[l].Row] = (top, top + _ui.Main.LineHeight);
            else if (lines[l].Heading is { } text)
                headings.Add((text, top));
        }
        return (places, headings, first > 0, first + ControlsLines < lines.Count);
    }

    /// <summary>
    /// The Controls page: the device, then each situation's controls under its heading with their bindings on that device, the
    /// prompt in the cell it binds, arrows saying there's more above or below, and the foot: what the last change came to, or on
    /// a binding what clears it.
    /// </summary>
    private void Controls(OptionsMenu options)
    {
        var (places, headings, above, below) = ControlPlaces(options);
        var cursor = options.Pulse.Now(Style.Text, Style.OptionsCursor);
        for (var i = 0; i < places.Count; i++)
        {
            var top = places[i].Top;
            if (top < 0)
                continue;
            var row = options.Rows.Rows[i];
            // The row waiting for an input stops pulsing, in the headings' darker brown, so the prompt reads at once.
            var waiting = options.Prompt is { } prompt && prompt.Control == row.Control;
            var color = waiting ? Style.Heading : i == options.Rows.Cursor ? cursor : Style.Text;
            _ui.Text(this, _ui.Main, row.Label, ChoiceLabel, top, color);
            var value = waiting ? options.PromptText! : row.ValueName ?? "";
            var font = _ui.Main.Width(value) <= ControlsRight - ControlsValue ? _ui.Main : _ui.Slim;
            _ui.Text(this, font, value, ControlsValue, top, color);
        }
        // On the parchment a little right of the frame's centre, as the rows are; on the title's box, on its centre.
        var centre = Style.Parchment ? FrameWidth / 2 + 8 : OptionsCentre(options);
        foreach (var (text, top) in headings)
            CentredText(text, centre, top, Style.Heading);
        // The list's arrow, turned to point up and down.
        var arrow = _ui.Size(MenuImages.SliderRight);
        if (above)
            TurnedArrow(ControlsRight - arrow.Y, ControlsListTop + arrow.X - 2, -Mathf.Pi / 2);
        if (below)
            TurnedArrow(ControlsRight, ControlsListTop + ControlsLines * ControlsPitch - arrow.X - 2, Mathf.Pi / 2);
        var foot = options.Message ?? (options.Prompt is null && options.Rows.Current?.Kind == OptionKind.Binding ? options.ClearHint : null);
        if (foot is not null)
            CentredText(foot, centre, ControlsFoot, Style.Text);
    }

    /// <summary>The list's arrow (<c>0x3757</c>, pointing right) turned about its corner at (x, y) in the frame.</summary>
    private void TurnedArrow(int x, int y, float angle)
    {
        DrawSetTransform(_pageOrigin + new Vector2(x, y), angle);
        _ui.Image(this, MenuImages.SliderRight, 0, 0, color: Style.Text);
        DrawSetTransform(_pageOrigin);
    }
}
