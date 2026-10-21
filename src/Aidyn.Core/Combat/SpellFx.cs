using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Calendar;
using Aidyn.Core.Magic;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>What a visual is (<c>CombatSpellFx.AddVisual</c>'s role, B <c>+0x2E</c>).</summary>
public enum SpellFxRole
{
    /// <summary>A spell's actor at its caster (role 0).</summary>
    Caster,
    /// <summary>A spell's actor at its target (role 1).</summary>
    Target,
    /// <summary>A potion's actor at the one who drinks it, or a flask's at each one its blast hurts (role 2).</summary>
    Potion,
    /// <summary>A refused or failed cast's actor at its caster, whatever the spell (role 3).</summary>
    Refused,
    /// <summary>A resisted cast's actor at its target (role 4).</summary>
    Resisted,
    /// <summary>A spell's lasting scene over its target's head (role 5).</summary>
    Aura,
    /// <summary>A potion's lasting scene over its drinker's head (role 6).</summary>
    PotionAura,
}

/// <summary>
/// The engine's host for the visuals' actors: each one's animation, which the visuals tick, and whether its emitters still have
/// particles in flight. Headless, there are none, and an actor's visual ends at once.
/// </summary>
public interface ISpellFxHost
{
    /// <summary>The visual's actor, or null if it has none to play.</summary>
    IActorMotion? Motion(SpellFxVisual visual);

    /// <summary>Whether the visual's actor's emitters have particles alive (<c>Particles.SceneHasEmitters</c>).</summary>
    bool Emitting(SpellFxVisual visual);
}

/// <summary>
/// One visual (an entry of <c>CombatSpellFx</c>'s pool B, <c>0x800E321C</c>, 0x30 bytes): an actor that plays its cast once at a body's
/// feet, or a scene shown over a body's head in its turn while an effect lasts. The runtime draws it.
/// </summary>
public sealed class SpellFxVisual
{
    internal SpellFxVisual(int handle, SpellFxRole role, RecordId id, Combatant owner, float height)
    {
        Handle = handle;
        Role = role;
        Id = id;
        Owner = owner;
        Height = height;
    }

    /// <summary>Its place in pool B, which the runtime keys its view by.</summary>
    public int Handle { get; }

    public SpellFxRole Role { get; }

    /// <summary>The actor (roles 0–4) or the scene (5 and 6) it shows.</summary>
    public RecordId Id { get; }

    /// <summary>
    /// The combatant whose body it sits on, whose actor's scene its emitters' locators name with bit 7 (the visual's scene
    /// <c>+0x2A4</c>, <c>CombatSpellFx.AttachScenes</c>, <c>0x8008BB08</c>).
    /// </summary>
    public Combatant Owner { get; }

    /// <summary>The other combatant of its cast, whose actor's scene bit 8 names (<c>+0x2A8</c>); for a potion, the owner.</summary>
    public Combatant Other { get; internal set; } = null!;

    /// <summary>Whether it's an actor that plays once, rather than a lasting scene.</summary>
    public bool IsActor => Role < SpellFxRole.Aura;

    /// <summary>
    /// An actor's place in the world: its owner's feet, the body's centre less its unscaled radius, raised by <see cref="Height"/>;
    /// followed while its animation runs, then kept. Never turned or scaled.
    /// </summary>
    public Vector3 Position { get; internal set; }

    /// <summary>How far above the feet an actor stands: the owner's entity's height for a spell that's shown at the head, else 0.</summary>
    public float Height { get; }

    /// <summary>An actor whose animation has run its length: it stays where it was, until its particles have gone.</summary>
    public bool Played { get; internal set; }

    /// <summary>A lasting scene loaded and in its turn (flag <c>0x40</c>), which the runtime draws at the owner's locator 3.</summary>
    public bool Showing => Playing && Loaded;

    /// <summary>A lasting scene's alpha in its turn: a hump of sin(t / 125 × 3.14), less after a cast, as a byte.</summary>
    public byte Alpha { get; internal set; }

    // B +0x28's flags.
    internal bool Live, RequestPending, Waiting, Playing, Loaded, Finished, StopAsked;
}

/// <summary>
/// One cast's visuals at one target (an entry of pool A, <c>0x800E3218</c>, whose index is the handle the effect record keeps): the
/// caster's and the target's actors and the target's lasting scene. It lives until each has ended, and for Darkness and Light until
/// their effect has.
/// </summary>
public sealed class SpellFxCast : IEffectVisual
{
    private readonly SpellFx _fx;

    internal SpellFxCast(SpellFx fx, int handle, RecordId source, IReadOnlyList<Effect> effects)
    {
        _fx = fx;
        Handle = handle;
        Source = source;
        Effects = effects;
    }

    /// <summary>Its place in pool A: the handle the effect record keeps (<c>+0x14</c>).</summary>
    public int Handle { get; }

    /// <summary>The spell or potion it shows (A <c>+0</c>, the kind).</summary>
    public RecordId Source { get; }

    internal IReadOnlyList<Effect> Effects { get; }

    internal int Caster = -1, Target = -1;
    internal int First = -1, Second = -1, Lasting = -1;
    internal bool Live, Ended;

    /// <summary>Whether it holds the sky's time of day while its effect lasts: Darkness's and Light's.</summary>
    internal bool HoldsTime => Effects.Any(e => e is PartOfDayEffect);

    /// <summary>Whether it holds its target's idle pose while its effect lasts: the webs and holds.</summary>
    internal bool HoldsPose => Effects.Any(e => e is ImmobiliseEffect);

    void IEffectVisual.Ended() => _fx.EndEffect(this);
}

/// <summary>
/// The spells' and potions' visuals in a battle (<c>combatspellvisuals.cpp</c>, 1.0 <c>0x8008AD60</c>–<c>0x8008D1DC</c>): an actor at
/// the caster and one at the target that play their cast once, with particles that fly between the two bodies' locators; a lasting
/// scene over the target's head while the effect lasts, shown one at a time across the whole battle, round-robin; the bodies' glow
/// and the sky's dimming as a spell is cast; Darkness's and Light's hold on the sky's time of day; and the webs' and holds' hold on
/// their target's pose. Three pools, as the original's: casts (A, 64), visuals (B, 128) and bodies (C, 16, the original's
/// "players"). See docs/systems/combat.md.
/// </summary>
/// <remarks>
/// The original runs it once a drawn frame with the frame's ticks (<c>CombatSpellFx.Frame</c>, <c>0x8008C754</c>); the port runs it
/// every tick, so its timers are the same at 30 fps, and an actor's animation is stepped as the original steps it, a tick at a time.
/// </remarks>
public sealed class SpellFx
{
    /// <summary>A lasting scene's turn, in ticks (<c>CombatSpellFx.AuraLength</c>, <c>0x800E3234</c>).</summary>
    public const int AuraTicks = 125;

    /// <summary>How long after a cast no lasting scene's turn moves on (<c>CombatSpellFx.CastWindow</c>, <c>0x800E323C</c>).</summary>
    public const int CastWindowTicks = 300;

    /// <summary>How long a cast's glow holds on its bodies before it fades (<c>CombatSpellFx.GlowBody</c>, <c>0x8008B364</c>).</summary>
    public const int GlowTicks = 150;

    /// <summary>How long a glow or the sky's dimming takes to come and go (<c>Body.SetTint</c>'s and <c>Sky.FadeBrightness</c>'s 30.0).</summary>
    public const float FadeTicks = 30;

    /// <summary>How long the sky stays dimmed after a cast (<c>CombatSpellFx.SkyTimer</c>, <c>0x800E323E</c>).</summary>
    public const int SkyDimTicks = 150;

    /// <summary>How far a cast dims the sky (<c>CombatSpellFx.DimSky</c>, <c>0x8008AECC</c>).</summary>
    public const float SkyDim = 0.5f;

    /// <summary>The ticks Darkness and Light ease the sky's time of day over, there and back (<c>World.SetTimeOfDayGoal</c>'s 60.0).</summary>
    public const float TimeEaseTicks = 60;

    /// <summary>
    /// How long after loading a lasting scene the next may load (<c>CombatSpellFx.LoadDelay</c>, <c>0x800E3214</c>): 5 frames, here
    /// at 30 fps.
    /// </summary>
    public static readonly int LoadDelayTicks = Cadence.Frames(5);

    public const int MaxCasts = 64, MaxVisuals = 128, MaxBodies = 16;

    /// <summary>The scene the original drew in, which a lasting scene's alpha hump runs on: 3.14, not π.</summary>
    private const double HumpPi = 3.14;

    private readonly MagicRecord? _magic;
    private readonly GameClock? _clock;
    private readonly SpellFxCast?[] _casts = new SpellFxCast?[MaxCasts];
    private readonly SpellFxVisual?[] _visuals = new SpellFxVisual?[MaxVisuals];
    private readonly Player?[] _bodies = new Player?[MaxBodies];
    private readonly Pool _castPool = new(MaxCasts), _visualPool = new(MaxVisuals), _bodyPool = new(MaxBodies);
    private int _auraCount, _castWindow, _fadeRamp, _skyTimer;
    private readonly Lane _lane = new();
    private AuraTurns _turns;
    private bool _skyDimmed;

    /// <param name="clock">The game's clock, whose shown time Darkness and Light move; none in a test.</param>
    /// <param name="magic">The magic record, with the refused and resisted casts' actors.</param>
    internal SpellFx(GameClock? clock, MagicRecord? magic)
    {
        _clock = clock;
        _magic = magic;
    }

    /// <summary>Where each visual's start and end are cued (the battle's host).</summary>
    internal Action<CombatCue>? Cue { get; set; }

    /// <summary>The sky's brightness, which a cast dims for a while (<see cref="SkyColors.At"/>'s <c>dim</c>).</summary>
    public SkyBrightness Sky { get; } = new();

    /// <summary>What the lasting scenes' alpha is scaled by (<c>CombatSpellFx.AuraFade</c>, <c>0x800E3230</c>): 0 just after a cast.</summary>
    public float AuraFade { get; private set; } = 1;

    /// <summary>The visuals live now, in pool order: what the runtime draws.</summary>
    public IEnumerable<SpellFxVisual> Visuals => _visuals.OfType<SpellFxVisual>().Where(v => v.Live);

    /// <summary>The casts whose visuals live now.</summary>
    public IEnumerable<SpellFxCast> Casts => _casts.OfType<SpellFxCast>().Where(c => c.Live);

    /// <summary>Lasting scenes waiting their turn or in it, over the whole battle (<c>CombatSpellFx.AuraCount</c>).</summary>
    public int AurasPending => _auraCount;

    /// <summary>
    /// How the lasting scenes take their turns: one at a time across the battle, as the original's, or each character's in turn over
    /// its own head, all at once (the options' Spell auras). Changing it starts the turns again.
    /// </summary>
    public AuraTurns Turns
    {
        get => _turns;
        set
        {
            if (value == _turns)
                return;
            _turns = value;
            RestartTurns();
        }
    }

    // ---- Starting ----

    /// <summary>
    /// A spell's visuals at one of its targets (<c>CombatSpellFx.Start</c>, <c>0x8008C43C</c>), from its cast
    /// (<c>Combatant.CastCurrentSpell</c>): a refused or failed cast plays the record's refused actor at the caster, a resisted one
    /// the resisted actor at the target, and any other the spell's actors at both and its lasting scene at the target, glows both
    /// bodies, dims the sky, and for Darkness and Light holds the sky's time of day, for a web or hold the target's pose.
    /// </summary>
    public SpellFxCast? Start(Combatant caster, Combatant target, SpellRecord spell, SpellVisualKind kind)
    {
        if (NewCast(spell.Id, spell.Effects ?? []) is not { } cast)
            return null;
        cast.Caster = Acquire(caster);
        cast.Target = Acquire(target);
        var visuals = spell.Visuals;
        switch (kind)
        {
            case SpellVisualKind.Failed:
                cast.First = Add(cast.Caster, SpellFxRole.Refused, _magic?.Refused, atHead: false);
                Attach(cast.First, cast.Target);
                break;
            case SpellVisualKind.Resisted:
                cast.Second = Add(cast.Target, SpellFxRole.Resisted, _magic?.Resisted, atHead: false);
                Attach(cast.Second, cast.Caster);
                break;
            default:
                cast.First = Add(cast.Caster, SpellFxRole.Caster, visuals?.Caster, atHead: false);
                cast.Second = Add(cast.Target, SpellFxRole.Target, visuals?.Target, visuals?.TargetAtHead == true);
                cast.Lasting = Add(cast.Target, SpellFxRole.Aura, visuals?.Aura, atHead: false);
                Attach(cast.First, cast.Target);
                Attach(cast.Second, cast.Caster);
                QueueAura(cast.Target, cast.Lasting);
                DimSky();
                Glow(cast.Caster);
                Glow(cast.Target);
                if (cast.Effects.OfType<PartOfDayEffect>().FirstOrDefault() is { } partOfDay)
                    _clock?.ShowTimeOfDay(partOfDay.Night ? 1.0f : 0.5f, TimeEaseTicks);
                if (cast.HoldsPose)
                    Hold(cast.Target);
                break;
        }
        cast.Live = true;
        Started(cast);
        return cast;
    }

    /// <summary>
    /// A potion's visuals on one who drinks it or whom a flask's blast hurts (<c>CombatSpellFx.StartPotion</c>, <c>0x8008C664</c>,
    /// from <c>Combatant.AttachSpellVisual</c>, <c>0x80064B10</c>): its actor and its lasting scene, both on that body; no glow and no
    /// dimming.
    /// </summary>
    public SpellFxCast? StartPotion(Combatant on, ItemRecord potion)
    {
        if (NewCast(potion.Id, potion.Effects ?? []) is not { } cast)
            return null;
        cast.Caster = Acquire(on);
        cast.Target = Acquire(on);
        cast.Second = Add(cast.Target, SpellFxRole.Potion, potion.Visuals?.Target, atHead: false);
        cast.Lasting = Add(cast.Target, SpellFxRole.PotionAura, potion.Visuals?.Aura, atHead: false);
        Attach(cast.Second, cast.Caster);
        QueueAura(cast.Target, cast.Lasting);
        cast.Live = true;
        Started(cast);
        return cast;
    }

    /// <summary>A cast's visuals, cued once each knows the bodies of its cast.</summary>
    private void Started(SpellFxCast cast)
    {
        foreach (var handle in (ReadOnlySpan<int>)[cast.First, cast.Second, cast.Lasting])
        {
            if (handle >= 0 && _visuals[handle] is { } visual)
                Cue?.Invoke(new SpellVisualStarted(visual));
        }
    }

    // The original doesn't check its pools: a 65th cast's visuals read past pool A's free list. The port shows nothing for one.
    private SpellFxCast? NewCast(RecordId source, IReadOnlyList<Effect> effects)
    {
        var handle = _castPool.Take();
        return handle < 0 ? null : _casts[handle] = new SpellFxCast(this, handle, source, effects);
    }

    /// <summary>
    /// The body's entry in pool C (<c>CombatSpellFx.AcquireBody</c>, <c>0x8008B23C</c>): its live one with one more reference, or a
    /// new one.
    /// </summary>
    private int Acquire(Combatant c)
    {
        for (var i = 0; i < MaxBodies; i++)
        {
            if (_bodies[i] is { Live: true } p && p.Combatant == c)
            {
                p.Refs++;
                return i;
            }
        }
        var index = _bodyPool.Take();
        if (index >= 0)
            _bodies[index] = new Player(c) { Live = true, Refs = 1, Cursor = 0 };
        return index;
    }

    /// <summary>One reference fewer to the body's entry, which goes at none (<c>CombatSpellFx.ReleaseBody</c>, <c>0x8008B780</c>).</summary>
    private void Release(int index)
    {
        if (index < 0 || _bodies[index] is not { } p)
            return;
        if (--p.Refs > 0)
            return;
        _bodies[index] = null;
        _bodyPool.Give(index);
    }

    /// <summary>
    /// A visual of <paramref name="role"/> on <paramref name="owner"/>'s body (<c>CombatSpellFx.AddVisual</c>, <c>0x8008B81C</c>): at its
    /// feet, raised by its entity's height for a spell shown at the head; none if the record names none. Its handle, or −1.
    /// </summary>
    private int Add(int owner, SpellFxRole role, RecordId? id, bool atHead)
    {
        if (id is not { } record || owner < 0 || _bodies[owner] is not { } p)
            return -1;
        var c = p.Combatant;
        var height = atHead ? c.Look.Height * c.Look.Scale : 0;
        var handle = _visualPool.Take();
        if (handle < 0)
            return -1;
        var visual = _visuals[handle] = new SpellFxVisual(handle, role, record, c, height)
        {
            Position = Feet(c) + new Vector3(0, height, 0),
            RequestPending = role < SpellFxRole.Aura,
            Live = true,
        };
        visual.Other = c;
        return handle;
    }

    /// <summary>Its emitters' attached scenes: the owner's body, then the other's (<c>CombatSpellFx.AttachScenes</c>, <c>0x8008BB08</c>).</summary>
    private void Attach(int handle, int other)
    {
        if (handle >= 0 && other >= 0 && _visuals[handle] is { } v && _bodies[other] is { } p)
            v.Other = p.Combatant;
    }

    private static Vector3 Feet(Combatant c) => c.Body.Sphere.Position - new Vector3(0, c.Body.Sphere.Radius, 0);

    // ---- The lasting scenes' turns ----

    /// <summary>
    /// A lasting scene joins the battle's turns (<c>CombatSpellFx.QueueAura</c>, <c>0x8008B498</c>), starting at once if it's the only
    /// one.
    /// </summary>
    private void QueueAura(int body, int handle)
    {
        if (handle < 0 || body < 0)
            return;
        _auraCount++;
        var p = _bodies[body]!;
        p.Pending++;
        // The original's turns are the battle's; each character's are its own.
        if ((_turns == AuraTurns.Battle ? _auraCount : p.Pending) == 1)
            StartAura(handle);
    }

    /// <summary>A lasting scene's turn begins: it loads, then its hump runs from 0 (<c>CombatSpellFx.StartAura</c>, <c>0x8008BC54</c>).</summary>
    private void StartAura(int handle)
    {
        if (handle < 0 || _visuals[handle] is not { Live: true } v)
            return;
        v.Waiting = true;
        LaneOf(v).Time = 0;
    }

    /// <summary>Whose turns a lasting scene takes its turn in: the battle's (<c>AuraTime</c>, <c>LoadDelay</c>), or its character's.</summary>
    private Lane LaneOf(SpellFxVisual v) => _turns == AuraTurns.EachCharacter && PlayerOf(v) is { } p ? p.Lane : _lane;

    /// <summary>
    /// The turns start again as how they're taken changes: every lasting scene out of its turn, then the battle's first, or each
    /// character's first.
    /// </summary>
    private void RestartTurns()
    {
        foreach (var v in Visuals.Where(v => !v.IsActor))
            (v.Waiting, v.Playing, v.Loaded, v.Alpha) = (false, false, false, 0);
        _lane.Time = _lane.LoadDelay = 0;
        foreach (var p in _bodies.OfType<Player>())
            p.Lane.Time = p.Lane.LoadDelay = 0;
        if (_turns == AuraTurns.Battle)
        {
            if (Visuals.FirstOrDefault(v => !v.IsActor) is { } first)
                StartAura(first.Handle);
            return;
        }
        foreach (var p in _bodies.OfType<Player>().Where(p => p is { Live: true, Pending: > 0 }))
            StartAura(NextOf(p));
    }

    /// <summary>
    /// The next lasting scene to take its turn (<c>CombatSpellFx.NextAura</c>, <c>0x8008B510</c>): the next body after
    /// <paramref name="body"/> in pool C, round, that has any waiting, else the same body; then that body's next after its cursor in
    /// pool B, round.
    /// </summary>
    private int NextAura(int body)
    {
        var next = body;
        for (var step = 1; step <= MaxBodies; step++)
        {
            var i = (body + step) % MaxBodies;
            if (_bodies[i] is { Live: true, Pending: > 0 })
            {
                next = i;
                break;
            }
        }
        return _bodies[next] is { } p ? NextOf(p) : -1;
    }

    /// <summary>A body's next lasting scene after its cursor in pool B, round, which the cursor then holds.</summary>
    private int NextOf(Player p)
    {
        var cursor = p.Cursor;
        for (var step = 1; step <= MaxVisuals; step++)
        {
            var h = (cursor + step) % MaxVisuals;
            if (_visuals[h] is { Live: true, IsActor: false } v && v.Owner == p.Combatant)
            {
                cursor = h;
                break;
            }
        }
        p.Cursor = cursor;
        return cursor;
    }

    // ---- What a cast does to the bodies, the sky and the clock ----

    /// <summary>A cast's glow on a body (<c>CombatSpellFx.GlowBody</c>, <c>0x8008B364</c>): toward white over 30 ticks, held 150, its fog off.</summary>
    private void Glow(int body)
    {
        if (body < 0 || _bodies[body] is not { } p)
            return;
        p.Glow = GlowTicks;
        p.Glowing = true;
        p.Combatant.Tint.Set(FadeTicks, Vector3.One);
        p.Combatant.Unfogged = true;
    }

    /// <summary>The sky dimmed to half over 30 ticks for 150, and the lasting scenes held back for 300 (<c>CombatSpellFx.DimSky</c>).</summary>
    private void DimSky()
    {
        _skyDimmed = true;
        _skyTimer = SkyDimTicks;
        Sky.FadeTo(SkyDim, FadeTicks);
        _castWindow = CastWindowTicks;
    }

    /// <summary>A web or hold keeps its target's idle pose (<c>CombatSpellFx.HoldBody</c>, <c>0x8008B3F0</c>).</summary>
    private void Hold(int body)
    {
        if (body < 0 || _bodies[body] is not { } p)
            return;
        p.Holds++;
        p.Combatant.PoseHeld = true;
    }

    /// <summary>One hold fewer, the pose let go at none (<c>CombatSpellFx.UnholdBody</c>, <c>0x8008B440</c>).</summary>
    private void Unhold(int body)
    {
        if (body < 0 || _bodies[body] is not { } p)
            return;
        if (p.Holds-- == 1)
            p.Combatant.PoseHeld = false;
    }

    /// <summary>
    /// A cast's effect has ended (<c>CombatSpellFx.EndEffect</c>, <c>0x8008C7A4</c>, from <c>EffectRecord.FreeVisual</c>): Darkness and
    /// Light let the sky's time go, a web or hold its target's pose, and the lasting scene goes once its turn is over. The actors play
    /// on.
    /// </summary>
    internal void EndEffect(SpellFxCast cast)
    {
        if (!cast.Live || _casts[cast.Handle] != cast)
            return;
        if (cast.HoldsTime)
        {
            cast.Ended = true;
            _clock?.ReleaseShownTime(TimeEaseTicks);
        }
        if (cast.HoldsPose)
            Unhold(cast.Target);
        if (cast.Lasting >= 0 && _visuals[cast.Lasting] is { } v)
            v.StopAsked = true;
    }

    /// <summary>
    /// A combatant's body has gone, dead or fled (<c>CombatSpellFx.ClearForBody</c>, <c>0x8008C880</c>): its visuals stay where they
    /// were and play on.
    /// </summary>
    public void ClearForBody(Combatant c)
    {
        foreach (var p in _bodies)
        {
            if (p is { Live: true } && p.Combatant == c)
                p.Cleared = true;
        }
    }

    // ---- The frame ----

    /// <summary>
    /// A tick of the visuals (<c>CombatSpellFx.Frame</c>, <c>0x8008C754</c>): each visual's step (<c>ProcessSpellVisuals</c>,
    /// <c>0x8008BCA0</c>), the glows (<c>StepGlow</c>, <c>0x8008B6AC</c>), then the sky's timer, the cast's window and what's
    /// finished (<c>Housekeeping</c>, <c>0x8008AF1C</c>). <paramref name="fade"/> is the screen's brightness.
    /// </summary>
    public void Tick(ISpellFxHost? host)
    {
        for (var h = 0; h < MaxVisuals; h++)
        {
            if (_visuals[h] is { Live: true } v)
                StepVisual(v, host);
        }
        StepGlows();
        Housekeep();
        Sky.Step();
        _clock?.StepShownOffset();
    }

    private void StepVisual(SpellFxVisual v, ISpellFxHost? host)
    {
        var owner = PlayerOf(v);
        if (owner is { Cleared: false })
            v.Position = Feet(v.Owner) + new Vector3(0, v.Height, 0);
        if (v.IsActor)
        {
            v.RequestPending = false;
            var motion = host?.Motion(v);
            // Its cast (request 5) from the first tick, stepped until the state's animation has run its length.
            if (motion is not null && motion.Frame < motion.StateLength)
                motion.Tick(Battle.CastRequest);
            if (motion is null || motion.Frame >= motion.StateLength)
            {
                v.Played = true;
                if (host?.Emitting(v) != true)
                    v.Finished = true;
            }
            return;
        }
        if (v.Waiting)
        {
            var lane = LaneOf(v);
            if (lane.LoadDelay == 0)
            {
                // The scene loads (BorgAnimLoadScene): with no memory asked, it always does.
                v.Loaded = true;
                v.Waiting = false;
                v.Playing = true;
                lane.LoadDelay = LoadDelayTicks;
            }
            else
            {
                lane.LoadDelay--;
            }
        }
        var humpOver = false;
        if (v.Playing)
        {
            var lane = LaneOf(v);
            if (_castWindow == 0)
                lane.Time++;
            if (lane.Time >= AuraTicks)
            {
                v.Playing = false;
                humpOver = true;
            }
            else
            {
                v.Alpha = (byte)(int)(Math.Sin(lane.Time / (double)AuraTicks * HumpPi) * 255 * AuraFade);
            }
        }
        if (humpOver)
        {
            v.Loaded = false;
            v.Alpha = 0;
            if (_turns == AuraTurns.Battle)
                StartAura(NextAura(BodyIndex(v.Owner)));
            else if (PlayerOf(v) is { } player)
                StartAura(NextOf(player));
        }
        if (v.StopAsked && !v.Playing)
        {
            v.StopAsked = false;
            v.Waiting = false;
            v.Finished = true;
        }
    }

    private void StepGlows()
    {
        foreach (var p in _bodies)
        {
            if (p is not { Live: true, Glowing: true })
                continue;
            if (p.Glow < 1)
            {
                p.Combatant.Tint.Clear(FadeTicks);
                p.Combatant.Unfogged = false;
                p.Glowing = false;
            }
            else
            {
                p.Glow--;
            }
        }
    }

    private void Housekeep()
    {
        if (_skyDimmed)
        {
            if (_skyTimer < 1)
            {
                Sky.FadeTo(1.0f, FadeTicks);
                _skyDimmed = false;
            }
            else
            {
                _skyTimer--;
            }
        }
        if (_castWindow == 0)
        {
            _fadeRamp = Math.Max(0, _fadeRamp - 1);
        }
        else
        {
            _fadeRamp = Math.Min((int)FadeTicks, _fadeRamp + 1);
            _castWindow = Math.Max(0, _castWindow - 1);
        }
        AuraFade = MathF.Cos(_fadeRamp / FadeTicks * (MathF.PI / 2));
        for (var a = 0; a < MaxCasts; a++)
        {
            if (_casts[a] is not { Live: true } cast)
                continue;
            cast.First = FreeIfFinished(cast.First);
            cast.Second = FreeIfFinished(cast.Second);
            cast.Lasting = FreeIfFinished(cast.Lasting);
            if (cast.First < 0 && cast.Second < 0 && cast.Lasting < 0 && (!cast.HoldsTime || cast.Ended))
            {
                Release(cast.Caster);
                Release(cast.Target);
                cast.Live = false;
                _casts[a] = null;
                _castPool.Give(a);
            }
        }
    }

    /// <summary>
    /// A finished visual freed (<c>CombatSpellFx.FreeVisual</c>, <c>0x8008C310</c>): an actor's emitters stop with it; a lasting scene
    /// leaves the turns.
    /// </summary>
    private int FreeIfFinished(int handle)
    {
        if (handle < 0 || _visuals[handle] is not { } v || !v.Finished)
            return handle;
        if (!v.IsActor)
        {
            _auraCount--;
            if (PlayerOf(v) is { } p)
                p.Pending--;
        }
        v.Live = false;
        _visuals[handle] = null;
        _visualPool.Give(handle);
        Cue?.Invoke(new SpellVisualEnded(v));
        return -1;
    }

    /// <summary>
    /// The battle's end (<c>CombatSpellFx.Free</c>, <c>0x8008C8E4</c>): every visual goes, the bodies' glows go back over 30 ticks
    /// and their poses are let go, and the sky's time of day comes back at once.
    /// </summary>
    public void Free()
    {
        foreach (var p in _bodies)
        {
            if (p is { Live: true, Cleared: false })
            {
                p.Combatant.Tint.Clear(FadeTicks);
                p.Combatant.Unfogged = false;
                p.Combatant.PoseHeld = false;
            }
        }
        // World.ClearTimeOffset over a tick, for Darkness and Light, and any easing back is done with.
        _clock?.ClearShownOffset();
        Array.Clear(_casts);
        Array.Clear(_visuals);
        Array.Clear(_bodies);
        _auraCount = 0;
    }

    private Player? PlayerOf(SpellFxVisual v) => _bodies.FirstOrDefault(p => p is { Live: true } && p.Combatant == v.Owner);

    private int BodyIndex(Combatant c) => Array.FindIndex(_bodies, p => p is { Live: true } && p.Combatant == c);

    /// <summary>A body's entry (pool C, 0x14 bytes): the original's <c>SPELLVISUAL</c> "player".</summary>
    private sealed class Player(Combatant combatant)
    {
        public Combatant Combatant { get; } = combatant;
        public bool Live, Glowing, Cleared;
        public int Refs, Glow, Holds, Pending, Cursor;
        public Lane Lane { get; } = new();
    }

    /// <summary>A run of turns: the playing hump's ticks and the frames until the next scene may load.</summary>
    private sealed class Lane
    {
        public int Time, LoadDelay;
    }

    /// <summary>A pool's free indices, taken from the front and given back to it (<c>idx = free[count++]</c>, <c>free[--count] = idx</c>).</summary>
    private sealed class Pool
    {
        private readonly int[] _free;
        private int _used;

        public Pool(int size)
        {
            _free = new int[size];
            for (var i = 0; i < size; i++)
                _free[i] = i;
        }

        public int Take() => _used < _free.Length ? _free[_used++] : -1;

        public void Give(int index) => _free[--_used] = index;
    }
}

/// <summary>How the lasting scenes over heads take their turns (<see cref="SpellFx.Turns"/>, the options' Spell auras).</summary>
public enum AuraTurns
{
    /// <summary>One at a time across the whole battle, round every one, as the original's: a buff alone pulses every 2 seconds, four in 9.</summary>
    Battle,
    /// <summary>Each character's in turn over its own head, all characters at once: the port's.</summary>
    EachCharacter,
}
