using HBHT.Core.Actors;
using HBHT.Core.Combat;
using HBHT.Core.Particles;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws the spells' and potions' visuals (<see cref="SpellFx"/>, <c>CombatSpellFx.Draw</c>, <c>0x8008C25C</c>): each actor where the core
/// puts it, unturned and unscaled, playing its cast as the core ticks it, its emitters attached to the bodies of its cast; and each
/// lasting scene in its turn at its owner's locator 3, at its alpha. Both are lit by their own scenes' lights and tinted by the
/// screen's fade, with their billboards turned to the camera and their textures scrolling. Each is made as the battle cues its start
/// (<see cref="SpellVisualStarted"/>) and freed as it cues its end (<see cref="SpellVisualEnded"/>). An actor's animations are the core's
/// (<see cref="SpellFxVisual.Actor"/>), which its scene is posed by; its emitters' particles here are drawn, and the core works out how
/// long they last on a head of its own (ADR 0040).
/// </summary>
public partial class SpellFxView : Node3D
{
    private readonly PluginSet _plugins;
    private readonly Models _models;
    private readonly ParticlesView? _particles;
    private readonly Func<Combatant, CombatantView?> _bodies;
    private readonly Dictionary<SpellFxVisual, Shown> _shown = [];

    /// <param name="bodies">A combatant's view, whose locators a visual's emitters attach to and a lasting scene stands at.</param>
    public SpellFxView(PluginSet plugins, Models models, ParticlesView? particles, Func<Combatant, CombatantView?> bodies)
    {
        Name = "SpellFx";
        _plugins = plugins;
        _models = models;
        _particles = particles;
        _bodies = bodies;
    }

    public SpellFxView() : this(PluginSet.Load([]), null!, null, _ => null)
    {
    }

    /// <summary>A visual starts (<see cref="SpellVisualStarted"/>): its scene or actor is made.</summary>
    public void Started(SpellFxVisual visual) => Show(visual);

    /// <summary>A visual has finished (<see cref="SpellVisualEnded"/>): its scene goes, and an actor's emitters stop with it, particles and all (<c>CombatSpellFx.FreeVisual</c>).</summary>
    public void Ended(SpellFxVisual visual)
    {
        if (!_shown.Remove(visual, out var shown))
            return;
        if (shown.Anchor is { } anchor)
            _particles?.Head.Stop(anchor);
        shown.Node.QueueFree();
    }

    /// <summary>
    /// Places every visual showing as the battle's recording has it on the tick drawn (<paramref name="frame"/>, ADR 0040), an actor's
    /// emitters started first as its actor started them in that tick, at the screen's <paramref name="fade"/>, its billboards turned toward
    /// <paramref name="eye"/>.
    /// </summary>
    public void Sync(BattleFrame frame, float fade, Vector3 eye)
    {
        // Scene.SetTint(F, F, F, a), F the screen's fade as a byte.
        var f = (int)(fade * 255) / 255f;
        foreach (var drawn in frame.Visuals)
        {
            var visual = drawn.Visual;
            if (!_shown.TryGetValue(visual, out var shown))
                continue;
            if (visual.IsActor)
            {
                shown.Node.Position = drawn.Position.ToGodot();
                if (shown.Animator is { } animator)
                {
                    foreach (var start in frame.Emitters)
                    {
                        if (start.Owner == visual)
                            animator.StartEmitters(start.Request);
                    }
                    animator.Follow(drawn.ActorState, drawn.ActorClock);
                }
            }
            else
            {
                if (_bodies(visual.Owner)?.Locator(3) is { } head)
                    shown.Head = head.ToGodot();
                shown.Head ??= drawn.Position.ToGodot();
                shown.Node.Position = shown.Head.Value;
                shown.Level!.Show(drawn.Showing ? drawn.Alpha : (byte)0);
            }
            N64Material.SetSceneTint(shown.Node, new Color(f, f, f));
            Billboards.Face(shown.Facings, eye);
            shown.Lights.Apply();
        }
    }

    /// <summary>Moves the visuals' scrolling textures on, as each draw of a scene loads them, at the textures' frame.</summary>
    public void Draw()
    {
        foreach (var shown in _shown.Values)
        {
            if (shown.Node.Visible)
                shown.Scrolling?.Draw();
        }
    }

    /// <summary>Frees every visual's scene, as the battle ends.</summary>
    public void Clear()
    {
        foreach (var shown in _shown.Values)
        {
            if (shown.Anchor is { } anchor)
                _particles?.Head.Stop(anchor);
            shown.Node.QueueFree();
        }
        _shown.Clear();
    }

    private Shown? Show(SpellFxVisual visual)
    {
        if (_shown.TryGetValue(visual, out var shown))
            return shown;
        if (!visual.IsActor)
        {
            if (_models.Instantiate(visual.Id) is not { } scene)
                return null;
            shown = new Shown(scene, _models.Glb(visual.Id)!.Extras) { Level = new Level(scene, null) };
            shown.Scrolling = _models.Scrolls.Attach(visual.Id, scene);
            shown.Head = _bodies(visual.Owner)?.Locator(3)?.ToGodot();
        }
        else
        {
            if (_plugins.TryGet<ActorRecord>(visual.Id) is not { } actor
                || _models.InstantiateActor(actor, motion: visual.Actor, recorded: true) is not var (node, animator))
                return null;
            shown = new Shown(node, _models.Glb(actor.Scene)!.Extras) { Animator = animator };
            shown.Scrolling = _models.Scrolls.Attach(actor.Scene, node);
            if (animator is not null)
            {
                animator.Driven = true;
                // Its emitters attach to the body it sits on and the other of its cast (CombatSpellFx.AttachScenes); they're its own,
                // which start as the recording says its actor started them, those for its first request, 0, as it's made.
                animator.Particles = _particles?.For(actor, node, _bodies(visual.Owner)?.Anchor, _bodies(visual.Other)?.Anchor);
                shown.Anchor = animator.Particles?.Anchor;
            }
        }
        AddChild(shown.Node);
        shown.Node.Position = visual.Position.ToGodot();
        return _shown[visual] = shown;
    }

    private sealed class Shown(Node3D node, Plugins.Assets.SceneExtras extras)
    {
        public Node3D Node { get; } = node;
        public OwnLights Lights { get; } = new(node, extras);
        public List<Node3D> Facings { get; } = Billboards.Facings(node);
        public ActorAnimator? Animator { get; init; }
        public Level? Level { get; init; }
        public ScrollingScene? Scrolling { get; set; }
        public IParticleAnchor? Anchor { get; set; }
        public Vector3? Head { get; set; }
    }
}
