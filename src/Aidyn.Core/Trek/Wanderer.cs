using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>
/// A monster party's body in the world, wandering from node to node (the original's <c>Wanderer</c>):
/// an NPC to talk to, or the leader of a group of enemies, which the player sees alone.
/// </summary>
public sealed class Wanderer
{
    /// <summary>How long it stands where it gave up a chase before going back to its node (<c>WanderTick</c>'s 300).</summary>
    public const int GiveUpTicks = 300;

    /// <summary>How near a chasing hunter ambushes the player.</summary>
    public const float AmbushDistance = 7;

    /// <summary>How near the player a wanderer that doesn't hunt turns to face him.</summary>
    public const float FaceDistance = 3;

    /// <summary>The sphere swept from a hunter to the player to see that nothing's between them.</summary>
    private const float AmbushSweepRadius = 0.5f;

    internal Wanderer(LoadedMap map, MonsterPartyObject party, EntityBody look, Body body, int perception)
    {
        Map = map;
        Party = party;
        Look = look;
        Body = body;
        Perception = perception;
        Hunts = party.Dialogue is null;
        Home = new Vector2(party.Position.X, party.Position.Z) + new Vector2(map.Origin.X, map.Origin.Z);
    }

    /// <summary>The chunk it belongs to, which it stays in.</summary>
    public LoadedMap Map { get; }

    public MonsterPartyObject Party { get; }

    /// <summary>The party's entity's model and size.</summary>
    public EntityBody Look { get; }

    public Body Body { get; }

    /// <summary>Its entity's footsteps and voice, if any.</summary>
    public BodySounds? Sounds { get; init; }

    /// <summary>What its sounds keep between ticks.</summary>
    public Audio.BodyAudio Audio { get; } = new();

    /// <summary>Whether it talks rather than fights: a party with a dialogue.</summary>
    public bool Talks => Party.Dialogue is not null;

    /// <summary>
    /// Whether it notices and chases the player (<c>moveType</c> 1): a party without a dialogue, until
    /// it ambushes him. Otherwise (<c>moveType</c> 0) it turns to face him when he's near.
    /// </summary>
    public bool Hunts { get; private set; }

    /// <summary>How far it notices the player: its entity's perception, and 10 for a dialogue entity (<c>EntityDB::GetPerception</c>).</summary>
    public int Perception { get; }

    /// <summary>The party's position, in the world's X and Z (<c>Wanderer.position</c>).</summary>
    public Vector2 Home { get; }

    /// <summary>How far it strays from <see cref="Home"/> after the player: the party's radius (<c>size</c>).</summary>
    public float Leash => Party.Radius;

    /// <summary>The node it's going to or turning to.</summary>
    public WanderNodeObject? Node { get; private set; }

    /// <summary>
    /// Where it's going, in the world's X and Z: the node, scattered on a visit after the first. For a
    /// node it turns to, the way from the node to the body instead (<c>start_position</c>).
    /// </summary>
    public Vector2 Goal { get; private set; }

    /// <summary>
    /// Ticks before it may move on, counting from when it chose its node or gave up a chase. It
    /// counts on below 0 (a u16 the original reads as signed).
    /// </summary>
    public ushort Timer { get; internal set; }

    /// <summary>
    /// Whether its next move goes back to <see cref="Node"/> rather than on: it gave up a chase
    /// (the node flags' 4 in <c>voxFlag</c>).
    /// </summary>
    public bool GoesBack { get; private set; }

    /// <summary>Whether it's after the player (<c>Wanderer+0x3E</c>).</summary>
    public bool Chasing { get; private set; }

    /// <summary>
    /// Whether it won't notice the player until it next sets off (the wanderer's flag 1,
    /// <c>setFlag1</c>), as after a conversation.
    /// </summary>
    public bool Calmed { get; set; }

    /// <summary>How far it notices the player doing each thing, set as he first comes near (<c>perceptionsSet</c>), or null.</summary>
    public NoticeRanges? Ranges { get; private set; }

    /// <summary>
    /// Takes the node's values (<c>WanderGetNextNode</c>): where it is, how near to come, the scatter,
    /// the chance of the second next node, the wait and what to do there. A node it turns to gives
    /// the way from the node to the body instead of a place.
    /// </summary>
    internal void Take(WanderNodeObject node)
    {
        Node = node;
        Timer = (ushort)node.Wait;
        GoesBack = false;
        Goal = new Vector2(node.Position.X + Map.Origin.X, node.Position.Z + Map.Origin.Z);
        if ((node.Flags & WanderFlags.Rotate) != 0)
            Goal = Vector2.Normalize(new Vector2(Body.Sphere.Position.X, Body.Sphere.Position.Z) - Goal);
    }

    /// <summary>
    /// Chooses the next node and where in it to go (<c>FUN_80012c58</c>, checked in 1.0's code): the
    /// second of this node's next nodes if a roll of 0 to 1 is under its chance, and the first
    /// otherwise; then, if it walks to the new node, a scatter of a random angle and a random length
    /// up to the new node's scatter. The first node, as the body appears, has none.
    /// </summary>
    internal void Choose(IRandom random)
    {
        if (Node is not { } node)
            return;
        var roll = random.Float(1);
        var link = node.Next.Count switch
        {
            0 => (RecordId?)null,
            1 => node.Next[0],
            _ => node.Next[roll < node.Chance ? 1 : 0],
        };
        var next = link is { } id ? Map[MapRecord.IndexOf(id)] as WanderNodeObject ?? node : node;
        Take(next);
        if ((next.Flags & WanderFlags.Move) == 0)
            return;
        var angle = random.Float(2 * MathF.PI);
        var length = random.Float(next.Scatter);
        Goal += new Vector2(MathF.Sin(angle), MathF.Cos(angle)) * length;
    }

    /// <summary>
    /// Sets off for its goal, or turns (<c>FUN_80012d44</c>): to a node it walks to, if it's further
    /// than the node's radius, walking if the party has <see cref="PartyFlags.Walks"/> and running
    /// otherwise; at a node it turns to, facing the node. It's no longer <see cref="Calmed"/>.
    /// </summary>
    internal void Resume()
    {
        Calmed = false;
        if (Node is not { } node)
            return;
        if ((node.Flags & WanderFlags.Move) != 0
            && Vector2.Distance(new Vector2(Body.Sphere.Position.X, Body.Sphere.Position.Z), Goal) > node.WanderRadius)
            Body.GoTo(Goal, node.WanderRadius, (Party.Flags & PartyFlags.Walks) != 0);
        // SetFacing(start_position), the way from the node to the body, in the original's reversed facing.
        if ((node.Flags & WanderFlags.Rotate) != 0)
            Body.Face(-Goal);
    }

    /// <summary>
    /// One tick of <c>WanderTick</c> for this wanderer, checked in 1.0's code: its timer, noticing
    /// the player, giving up and ending a chase, turning to face him, moving on, and chasing. Returns
    /// whether it ambushes him.
    /// </summary>
    internal bool Tick(in WanderTick tick)
    {
        Timer--;
        var at = Body.Sphere.Position;
        var playerProx = Vector3.Distance(at, tick.Player);
        // The original measures from 0.3 × perception behind its facing, which it keeps reversed.
        var ahead = at + new Vector3(Body.Heading.X, 0, Body.Heading.Y) * (Perception * 0.3f);
        var perceived = Vector3.Distance(ahead, tick.Player);
        var bodyOut = Vector2.Distance(Home, new Vector2(at.X, at.Z));
        // The original measures this from the party's position in its own chunk's coordinates to the
        // player's in his, so it was wrong for a wanderer in another chunk.
        var playerOut = Vector2.Distance(Home, new Vector2(tick.Player.X, tick.Player.Z));

        if (perceived <= 2 * Perception && Ranges is null)
            Ranges = new NoticeRanges(Perception * (1 - tick.FogTime * 0.75f), tick.Sneak);
        var most = Ranges?.Most ?? 0;

        // Giving up, and ending a chase. Both beyond the leash, it stands where it is for a while,
        // then goes back to its node; otherwise a chaser goes back at once.
        var bothOut = Leash <= bodyOut && Leash <= playerOut;
        if (((most <= perceived || bothOut) && Chasing) || (2 * Perception <= perceived && Ranges is not null))
        {
            if (bothOut)
            {
                Body.Stop();
                Timer = GiveUpTicks;
                GoesBack = true;
            }
            else if (Chasing)
            {
                Resume();
            }
            Ranges = null;
            if (most <= perceived || Leash + 2 <= bodyOut)
                Chasing = false;
        }

        // The original keeps one "noticed" for the whole loop, so every hunter after one that notices
        // the player chases him too; as the user chose, each notices for itself.
        var noticed = Hunts && bodyOut <= Leash && !Calmed && perceived <= (Ranges ?? default).For(tick.PlayerRequest);

        if (!Hunts && playerProx < FaceDistance)
        {
            Body.Request = PlayerMotion.Idle;
            Body.Face(new Vector2(tick.Player.X - at.X, tick.Player.Z - at.Z));
        }

        if (!noticed && !Chasing)
        {
            if (!Body.Moving && !Body.Turning && !Body.Held && (short)Timer <= 0)
            {
                if (GoesBack)
                    GoesBack = false;
                else
                    Choose(tick.Random);
                Resume();
            }
            return false;
        }

        Chasing = true;
        if (!Hunts)
            return false;
        Body.GoTo(new Vector2(tick.Player.X, tick.Player.Z), 2, walk: false);
        // The original also needs enemyHostileFlag, which nothing in 1.0 clears, and no dialogue,
        // which a hunter never has.
        if (playerProx >= AmbushDistance)
            return false;
        var from = at with { Y = at.Y + 1 };
        var to = tick.Player with { Y = tick.Player.Y + 1 };
        if (MathF.Abs(from.Y - to.Y) >= Crowd.Height
            || SpherePhysics.Sweep(tick.World, from, to, AmbushSweepRadius, skipSeeThrough: false, out _, out _))
            return false;
        // The battle starts here, as an ambush (collisionByte 2). Until combat (2.5), the party
        // stops where it is and no longer hunts.
        Hunts = false;
        Body.Stop();
        return true;
    }
}

/// <summary>
/// How far a wanderer notices the player, by what he's doing, set as he first comes within twice its
/// perception (<c>WanderTick</c>, from 1.0's code).
/// </summary>
/// <param name="Walking">Walking (request 2): its perception, less up to three quarters in fog.</param>
/// <param name="Sneak">What the party's best Stealth leaves of the sneaking and standing ranges (<see cref="SneakFor"/>).</param>
public readonly record struct NoticeRanges(float Walking, float Sneak)
{
    /// <summary>Running (request 3).</summary>
    public float Running => Walking * 1.5f;

    /// <summary>The furthest: beyond it a chase ends.</summary>
    public float Most => Walking * 2;

    /// <summary>Sneaking (request 25).</summary>
    public float Sneaking => Walking * 0.6f * Sneak;

    /// <summary>Standing, or anything else.</summary>
    public float Other => Walking * 0.55f * Sneak;

    /// <summary>The range for what the player asks for.</summary>
    public float For(int request) => request switch
    {
        PlayerMotion.Run => Running,
        PlayerMotion.Walk => Walking,
        PlayerMotion.Sneak => Sneaking,
        _ => Other,
    };

    /// <summary>
    /// What the party's best Stealth leaves of the sneaking and standing ranges (<c>sneak_value(0.8)</c>):
    /// 1 − min(Stealth / 10, 1) × 0.8, or 0 with no party.
    /// </summary>
    public static float SneakFor(Party? party) =>
        party?.MostSkilled(Skill.Stealth) is { } best ? 1 - MathF.Min(best.Skills.Modified(Skill.Stealth) / 10f, 1) * 0.8f : 0;
}

/// <summary>What <c>WanderTick</c> weighs each wanderer against, the same for all of them in a tick.</summary>
/// <param name="Player">The player's sphere's centre.</param>
/// <param name="PlayerRequest">What he's doing: the animation he asks for.</param>
/// <param name="Sneak">What the party's best Stealth leaves (<see cref="NoticeRanges.SneakFor"/>).</param>
/// <param name="FogTime">How thick the weather's fog is, 0 to 1.</param>
internal readonly record struct WanderTick(CollisionWorld World, Vector3 Player, int PlayerRequest, float Sneak, float FogTime, IRandom Random);
