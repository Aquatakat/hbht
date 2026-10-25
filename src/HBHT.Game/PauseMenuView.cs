using HBHT.Core.Ui;
using HBHT.Plugins;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws the pause menu (<see cref="Core.Ui.PauseMenu"/>, <c>BigAssMenu</c>): the System tab (<see cref="SystemTabView"/>),
/// the Party tab (<see cref="PartyTabView"/>) and the World tab (<see cref="WorldTabView"/>) side by side on a
/// <see cref="UiScreen"/> over the world, between the rollers of the 3D scroll (<see cref="PauseScrollView"/>), and the World
/// tab's journal over them. The menu's state and rules are the core's: which tab shows and how far the tabs have slid, what
/// the scroll is doing and how dark the world is, what the Party tab's window is and how opaque it is as the post-battle
/// windows swap; this reads them each tick (<see cref="Sync"/>) and lays the pieces out as the original does (ADR 0014), and
/// plays the scroll's steps the core cues (<see cref="PlayScroll"/>).
/// </summary>
/// <remarks>
/// <para>
/// The world behind the menu isn't a picture: the game stops trek and its scenery's animations, and goes on drawing the
/// world and the HUD as they stand, under a black layer as dark as the tint, which darkens them as multiplying by
/// the tint would. So the window's size and the menu size setting redraw them as they change, as they would in
/// trek.
/// </para>
/// <para>
/// The tabs are one scroll, as their images are drawn, 320 apart, so a screen wider than the frame shows the
/// edges of the tabs either side, their widgets faded into the parchment, which clears as a tab slides into the
/// middle. The scroll's rollers move with them, and the tabs are cut at the rollers. On a screen narrower than
/// 320 the frame is cut at the screen's edges.
/// </para>
/// </remarks>
public partial class PauseMenuView : CanvasLayer
{
    /// <summary>Above the lens flare and the debug overlay.</summary>
    public const int MenuLayer = 10;

    /// <summary>How far the widgets of a tab beside the one showing are faded into its parchment.</summary>
    private const float NeighbourVeil = 0.6f;

    private readonly PluginSet _plugins;
    private readonly Core.Ui.PauseMenu _menu;
    private readonly UiScreen _screen = new() { ForwardsMouse = true };
    /// <summary>
    /// What darkens the world behind the menu, still as the game stops it (<c>GetSnapShot</c>,
    /// <c>PauseMenu.DrawBackdrop</c>): black at one less the tint, over everything under the menu.
    /// </summary>
    private readonly ColorRect _darkness = new()
    {
        Name = "Darkness",
        Color = Colors.Transparent,
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
    /// <summary>The tabs, cut to what the scroll has unrolled.</summary>
    private readonly Control _tabs = new() { Name = "Tabs", ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly Models _models;
    private readonly PauseScrollView? _scrollView;
    /// <summary>The World tab's journal, over the tabs and the scroll, which fade out under it.</summary>
    private readonly JournalView _journal;
    private PartyTabView? _view;
    private SystemTabView? _system;
    private WorldTabView? _world;

    /// <param name="menu">The menu's state, which the session owns and steps.</param>
    public PauseMenuView(PluginSet plugins, Core.Ui.PauseMenu menu)
    {
        _plugins = plugins;
        _menu = menu;
        Name = "Pause menu";
        Layer = MenuLayer;
        Visible = false;
        _models = new Models(plugins);
        _darkness.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_darkness);
        _screen.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_screen);
        _screen.Canvas.AddChild(_tabs);
        _scrollView = PauseScrollView.Create(plugins, _models);
        if (_scrollView is not null)
        {
            _scrollView.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _screen.Canvas.AddChild(_scrollView);
        }
        _journal = new JournalView(plugins);
        _journal.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _screen.Canvas.AddChild(_journal);
    }

    public override void _ExitTree() => _models.Dispose();

    /// <summary>Whether the menu is drawn: from its opening until the core says it has closed.</summary>
    public bool IsOpen => Visible;

    /// <summary>The UI screen, whose lines and filter the game sets.</summary>
    public UiScreen Screen => _screen;

    /// <summary>The frame on screen when the menu opened, which a save's screenshot is cut from.</summary>
    public Image? Snapshot { get; private set; }

    /// <summary>The scroll actor's clock in its state, from 1, which the core's menu steps by; without the actor, past any state's end.</summary>
    public int ScrollFrame => _scrollView?.Frame ?? 1000;

    /// <summary>The length of the state the scroll actor is in, in frames.</summary>
    public int ScrollLength => _scrollView?.Length ?? 1;

    /// <summary>A step of the scroll's actor, as the core cues it (<see cref="ScrollPlayCue"/>).</summary>
    public void PlayScroll(ScrollCue cue) => _scrollView?.Play(cue);

    /// <summary>
    /// The menu has opened (the transition that opens it): the frame before it covers it is kept for a save's screenshot, the
    /// scroll starts afresh, as the original builds it for each opening, and the tabs' views take this opening's models.
    /// </summary>
    public void Open()
    {
        Snapshot = GetViewport().GetTexture().GetImage();
        _scrollView?.Restart();
        _scrollView?.Rendering = true;
        Bind();
        Visible = true;
        Activate();
        ShowScroll();
    }

    /// <summary>The tabs' views on the models this opening built: made the first time, and after that given the new model.</summary>
    private void Bind()
    {
        _view = Show(_view, _menu.Window, (p, m) => new PartyTabView(p, m) { StepsWindow = false });
        _system = Show(_system, _menu.System, (p, m) => new SystemTabView(p, m));
        _world = Show(_world, _menu.World, (p, m) => new WorldTabView(p, m));
    }

    /// <summary>
    /// A tab's view on a model: made the first time, laid out by <see cref="Place"/> at the screen's size rather than its
    /// parent's, and after that given the new model.
    /// </summary>
    private T Show<T, TModel>(T? view, TModel model, Func<PluginSet, TModel, T> make) where T : Control, IPauseTab<TModel>
    {
        if (view is not null)
        {
            ((IPauseTab<TModel>)view).Model = model;
            return view;
        }
        view = make(_plugins, model);
        _tabs.AddChild(view);
        view.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        return view;
    }

    /// <summary>
    /// Draws the menu as the core's tick left it: shown as it opens and hidden once it has closed, the Party tab's window as
    /// it stands, swapped after a battle, and how opaque it is, which tab steps and takes the mouse, the journal over the
    /// tabs, and the scroll, the tint and where the tabs are cut (<see cref="ShowScroll"/>).
    /// </summary>
    public void Sync()
    {
        if (!_menu.IsOpen)
        {
            if (Visible)
                Close();
            return;
        }
        if (!Visible)
            Open();
        var view = _view!;
        if (!ReferenceEquals(view.Window, _menu.Window))
            view.Window = _menu.Window;
        view.WindowAlpha = _menu.WindowAlpha;
        Activate();
        var world = _world!;
        world.Active = _menu.Tab == PauseTab.World;
        if (world.Menu.Journal is not null || _journal.Journal is not null)
            _journal.Journal = world.Menu.Journal;
        ShowScroll();
        Redraw();
    }

    /// <summary>The menu has closed: nothing of it is drawn, and the scroll's viewports stop rendering.</summary>
    private void Close()
    {
        Visible = false;
        _scrollView?.Rendering = false;
        Snapshot = null;
        _journal.Journal = null;
        _view?.ProcessMode = ProcessModeEnum.Disabled;
        _system?.ProcessMode = ProcessModeEnum.Disabled;
    }

    /// <summary>Draws the tabs and the journal again, after a press.</summary>
    public void Redraw()
    {
        _view?.QueueRedraw();
        _system?.QueueRedraw();
        _world?.QueueRedraw();
        _journal.QueueRedraw();
    }

    /// <summary>The tabs' views, left to right, by <see cref="PauseTab"/>.</summary>
    private (Control View, PauseTab Tab)[] Tabs => [(_system!, PauseTab.System), (_view!, PauseTab.Party), (_world!, PauseTab.World)];

    /// <summary>Only the tab showing processes, and takes the mouse.</summary>
    private void Activate()
    {
        foreach (var (view, tab) in Tabs)
            ((IPauseTab)view).Activate(_menu.Tab == tab);
    }

    /// <summary>
    /// Shows the opening or closing as it is: the world's tint and whether, and how opaque, the tabs and scroll are
    /// drawn. They fade out as the journal fades in over them, and back as it fades out.
    /// </summary>
    private void ShowScroll()
    {
        var scroll = _menu.Scroll;
        _darkness.Color = new Color(0, 0, 0, 1 - scroll.Tint / 255f);
        var alpha = scroll.Alpha * (1 - _journal.Shown);
        _tabs.Visible = scroll.ShowsMenu && alpha > 0;
        _tabs.Modulate = new Color(1, 1, 1, alpha);
        if (_scrollView is { } view)
        {
            view.Visible = scroll.ShowsMenu && alpha > 0;
            view.Alpha = alpha;
        }
        Place();
    }

    /// <summary>
    /// Lays the tabs side by side, 320 apart, with the one at the menu's position showing at the screen's centre and
    /// those beside it at the screen's edges, faded, and cuts them to what the scroll has unrolled: within the
    /// original's clip, and between the scroll's rollers, so that its parchment never shows past them. Barter and
    /// training show the Party tab alone. The scroll moves with them.
    /// </summary>
    private void Place()
    {
        var screen = _screen.Canvas.GetViewportRect().Size;
        var position = _menu.Position;
        // The frame's left edge, as the tabs centre it.
        var frameLeft = ((int)screen.X - UiScreen.Width) / 2;
        var (left, right) = _menu.Scroll.Clip is var (l, r)
            ? (l <= 0 ? float.NegativeInfinity : frameLeft + l, r >= UiScreen.Width ? float.PositiveInfinity : frameLeft + r)
            : (float.NegativeInfinity, float.PositiveInfinity);
        if (_scrollView is { } view)
        {
            view.X = position <= 1
                ? Mathf.Lerp(PauseScroll.TabX[0], PauseScroll.TabX[1], position)
                : Mathf.Lerp(PauseScroll.TabX[1], PauseScroll.TabX[2], position - 1);
            var (systemEnd, worldEnd) = view.Arrange();
            left = Math.Max(left, Mathf.Round(systemEnd));
            right = Math.Min(right, Mathf.Round(worldEnd));
        }
        left = Math.Clamp(left, 0, screen.X);
        right = Math.Clamp(right, 0, screen.X);
        _tabs.Position = new Vector2(left, 0);
        _tabs.Size = new Vector2(Math.Max(right - left, 0), screen.Y);
        foreach (var (control, tab) in Tabs)
        {
            var offset = ((int)tab - position) * UiScreen.Width;
            control.Position = new Vector2(Mathf.Round(offset) - left, 0);
            control.Size = screen;
            control.Visible = Math.Abs(offset) < (screen.X + UiScreen.Width) / 2 && (!_menu.TabsLocked || tab == PauseTab.Party) && tab <= _menu.LastTab;
            ((IPauseTab)control).Veil = Math.Min(1, Math.Abs((int)tab - position)) * NeighbourVeil;
        }
    }
}
