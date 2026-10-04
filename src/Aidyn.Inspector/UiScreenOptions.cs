using Aidyn.Core.Ui;
using Aidyn.Game;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// How a page's UI screen is shown, to compare with the console: the filter that scales it up, how
/// large the preview is, the window's shape and how many lines the screen has.
/// </summary>
public partial class UiScreenOptions : HFlowContainer
{
    private readonly UiScreen _screen;
    private readonly OptionButton _filter, _zoom, _aspect, _lines;
    private int _scale = 1;
    private bool _wide;

    /// <param name="shape">What the window's shape changes, for its tooltip.</param>
    public UiScreenOptions(UiScreen screen, string shape)
    {
        _screen = screen;
        // The screen keeps the shape chosen, rather than the page's.
        _screen.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _screen.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        AddThemeConstantOverride("h_separation", 6);
        AddChild(_filter = Choice("How the UI screen is scaled up to the window.",
            [((int)UiFilter.VideoOut, "As the console outputs it"), ((int)UiFilter.Pixels, "Sharp pixels"), ((int)UiFilter.Smooth, "Smooth")],
            id => _screen.Filter = (UiFilter)id));
        AddChild(_zoom = Choice("How large the preview is.", [(1, "1×"), (2, "2×"), (3, "3×")], id =>
        {
            _scale = id;
            Fit();
        }));
        AddChild(_aspect = Choice($"The window's shape: {shape}", [(0, "4:3"), (1, "16:9")], id =>
        {
            _wide = id == 1;
            Fit();
        }));
        AddChild(_lines = Choice("How many pixels tall the UI screen is: 240 is the original's, and more draws the UI smaller.",
            [(240, "240 lines"), (360, "360 lines"), (480, "480 lines")], id => _screen.Lines = id));
        Fit();
    }

    /// <summary>Sets the options (<c>--zoom</c>, <c>--ui-filter</c>, <c>--aspect</c>, <c>--ui-lines</c>). Ones not offered are ignored.</summary>
    public void Set(int? zoom, UiFilter? filter, bool? wide, int? lines)
    {
        Pick(_zoom, zoom);
        Pick(_filter, (int?)filter);
        Pick(_aspect, wide is { } w ? (w ? 1 : 0) : null);
        Pick(_lines, lines);

        static void Pick(OptionButton choice, int? id)
        {
            if (id is { } value && choice.GetItemIndex(value) is var i and >= 0)
            {
                choice.Select(i);
                choice.EmitSignal(OptionButton.SignalName.ItemSelected, i);
            }
        }
    }

    private void Fit() =>
        _screen.CustomMinimumSize = new Vector2(_wide ? MathF.Round(UiScreen.Height * 16 / 9f) : UiScreen.Width, UiScreen.Height) * _scale;

    private static OptionButton Choice(string tooltip, (int Id, string Name)[] items, Action<int> apply)
    {
        var choice = new OptionButton { TooltipText = tooltip };
        foreach (var (id, name) in items)
            choice.AddItem(name, id);
        choice.ItemSelected += i => apply(choice.GetItemId((int)i));
        return choice;
    }
}
