using Aidyn.Core.Actors;
using Aidyn.Core.Combat;
using Aidyn.Core.Particles;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws the spells' and potions' visuals (<see cref="SpellFx"/>, <c>CombatSpellFx.Draw</c>, <c>0x8008C25C</c>): each actor where the core
/// puts it, unturned and unscaled, playing its cast as the core ticks it, its emitters attached to the bodies of its cast; and each
/// lasting scene in its turn at its owner's locator 3, at its alpha. Both are lit by their own scenes' lights and tinted by the
/// screen's fade, with their billboards turned to the camera and their textures scrolling. It's also the core's host for the actors
/// (<see cref="ISpellFxHost"/>): an actor's scene is made as the core first asks for its animation.
/// </summary>
public partial class SpellFxView : Node3D, ISpellFxHost
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

    /// <summary>The actor's animation, its scene made the first time it's asked for, or null for a visual that isn't an actor or has none.</summary>
    public IActorMotion? Motion(SpellFxVisual visual) => Show(visual)?.Animator;

    /// <summary>Whether the actor's emitters still have particles in flight.</summary>
    public bool Emitting(SpellFxVisual visual) =>
        _particles is not null && _shown.TryGetValue(visual, out var shown) && shown.Anchor is { } anchor && _particles.Head.HasEmitters(anchor);

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
    /// Places every visual showing, at the screen's <paramref name="fade"/>, its billboards turned toward <paramref name="eye"/>.
    /// </summary>
    public void Sync(float fade, Vector3 eye)
    {
        // Scene.SetTint(F, F, F, a), F the screen's fade as a byte.
        var f = (int)(fade * 255) / 255f;
        foreach (var (visual, shown) in _shown)
        {
            if (visual.IsActor)
            {
                shown.Node.Position = visual.Position.ToGodot();
            }
            else
            {
                if (_bodies(visual.Owner)?.Locator(3) is { } head)
                    shown.Head = head.ToGodot();
                shown.Node.Position = shown.Head;
                shown.Level!.Show(visual.Showing ? visual.Alpha : (byte)0);
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
            shown.Head = (_bodies(visual.Owner)?.Locator(3) ?? visual.Position).ToGodot();
        }
        else
        {
            if (_plugins.TryGet<ActorRecord>(visual.Id) is not { } actor
                || _models.InstantiateActor(actor) is not var (node, animator))
                return null;
            shown = new Shown(node, _models.Glb(actor.Scene)!.Extras) { Animator = animator };
            shown.Scrolling = _models.Scrolls.Attach(actor.Scene, node);
            if (animator is not null)
            {
                animator.Driven = true;
                // Its emitters attach to the body it sits on and the other of its cast (CombatSpellFx.AttachScenes); those for its
                // first request, 0, start as it's made, before the core asks it for its cast.
                animator.Particles = _particles?.For(actor, node, _bodies(visual.Owner)?.Anchor, _bodies(visual.Other)?.Anchor);
                shown.Anchor = animator.Particles?.Anchor;
                animator.Motion.Made();
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
        public Vector3 Head { get; set; }
    }
}
