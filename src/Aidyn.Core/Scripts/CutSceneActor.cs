using System.Numerics;
using Aidyn.Core.Movement;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Scripts;

/// <summary>
/// One of a cut-scene's actors once <see cref="DialogueCommand.CreateActor"/> has spawned it
/// (<c>ActorAndID</c> and its <c>playerData</c>): a body with its entity's look, which the script walks,
/// turns, animates and hands items to.
/// </summary>
public sealed class CutSceneActor
{
    /// <summary>How far in front of its face a point-of-view camera sits (<c>0xBE19999A</c>, −0.15 along the original's reversed facing).</summary>
    public const float EyeForward = 0.15f;

    /// <summary>How far below the top of its height its eyes are (<c>GetHeightMinPoint2</c>, <c>0x800d2cf0</c>).</summary>
    public const float EyeBelowTop = 0.2f;

    /// <summary>Ticks left that it keeps a request the script asked for.</summary>
    private int _stimulus;

    internal CutSceneActor(RecordId entity, EntityBody look, Body body)
    {
        Entity = entity;
        Look = look;
        Body = body;
    }

    /// <summary>The entity or dialogue entity it is: one of the script's actors.</summary>
    public RecordId Entity { get; }

    /// <summary>Its entity's actor, radius, scale and height.</summary>
    public EntityBody Look { get; }

    public Body Body { get; }

    /// <summary>What its footsteps and voice keep between ticks.</summary>
    public Audio.BodyAudio Audio { get; } = new();

    /// <summary>
    /// Not drawn, as the camera looks out of its eyes (<c>ACTOR_2000</c>, which only
    /// <see cref="DialogueCommand.CameraCutToActor"/> sets). Its shadow and what it holds still are.
    /// </summary>
    public bool Hidden { get; internal set; }

    /// <summary>The scene of the item it holds at its scene's locator 0, if any (<c>AttachItemToPlayer</c>, slot 0).</summary>
    public RecordId? Held { get; internal set; }

    /// <summary>
    /// Where a point-of-view camera looks from: 0.15 in front of it, at its sphere's centre raised by its
    /// height less 0.2 and its scaled radius (<c>DialoguePreCallback</c>, <c>positionMirror</c>), which puts
    /// it at its eyes.
    /// </summary>
    public Vector3 Eyes
    {
        get
        {
            var at = Body.Sphere.Position;
            var height = Look.Height * Look.Scale - EyeBelowTop - Body.ScaledRadius;
            return new Vector3(at.X + Body.Heading.X * EyeForward, at.Y + height, at.Z + Body.Heading.Y * EyeForward);
        }
    }

    /// <summary>Asks it for an animation the script plays, which it keeps for <paramref name="ticks"/> (<c>ApplyStimulus</c>).</summary>
    internal void Stimulate(int request, int ticks)
    {
        Body.Request = request;
        _stimulus = ticks;
    }

    /// <summary>
    /// After a tick: it's asked to stand again, unless it's keeping what the script asked for
    /// (<c>FUN_80059b50</c>). A steered body's walk or run is asked for afresh as it steps.
    /// </summary>
    internal void Settle()
    {
        if (_stimulus > 0)
            _stimulus--;
        else
            Body.Request = Body.Idle;
    }
}

/// <summary>An actor's animations as a cut-scene needs them: their root motion, and how long the state playing lasts.</summary>
public interface IActorMotion : IRootMotion
{
    /// <summary>The length in ticks of the animation of the state it's in (<c>aniLength</c>).</summary>
    int StateLength { get; }

    /// <summary>The frame of the state it's in, from 1 (<see cref="Actors.ActorStateMachine.Clock"/>).</summary>
    int Frame => 1;

    /// <summary>For a body with no animations, or none yet: it stands, and its state lasts a tick.</summary>
    static readonly IActorMotion Still = new StillMotion();

    private sealed class StillMotion : IActorMotion
    {
        public int StateLength => 1;

        public float Tick(int request) => 0;
    }
}
