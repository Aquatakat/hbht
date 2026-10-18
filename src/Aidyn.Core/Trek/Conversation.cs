using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>
/// A script running in trek, in the conversation box over the world: talking to a wanderer
/// (<c>monsterpary_func</c>, and the dialogue mode's functions for a conversation), or a sign. The
/// world runs on under it, but the clock and <see cref="Wanderers.Tick"/> stop, and the player gets
/// no input. Checked in 1.0's code. A backdrop or the help menu runs here too, on its own page, with
/// the world stopped under it (<see cref="ShowsBackdrop"/>).
/// </summary>
/// <remarks>
/// <para>
/// Talking to a wanderer holds it: it's calmed, its body held, both are asked to stand, and each
/// turns to face the other. The camera frames the two (<see cref="FollowCamera.Frame"/>). Each
/// press moves the script on and asks the speaker for its talking for a tick (<see cref="SpeakerTalk"/>):
/// Alaron for his lines, and the wanderer for anyone else's, unless the script played an animation itself.
/// </para>
/// <para>
/// At the end the camera goes back to the player, and the wanderer sets off for its node again, or
/// if someone joined the party in the conversation, its body goes.
/// </para>
/// </remarks>
public sealed class Conversation
{
    private readonly PlayerMotion _player;
    private readonly Watch _host;

    private Conversation(GameState game, DialogueRecord script, ScriptStart start, IScriptHost host, PlayerMotion player, Wanderer? with)
    {
        _player = player;
        _host = new Watch(host);
        With = with;
        Runner = new ScriptRunner(script, game.Flags, game, _host, game.Random.World, id => game.Plugins.TryGet<DialogueRecord>(id), start,
            game.StateFlagsRecord.RandomSkillChecks);
    }

    public ScriptRunner Runner { get; }

    /// <summary>The wanderer talked to, or none for a sign.</summary>
    public Wanderer? With { get; }

    /// <summary>
    /// Who keeps the shop or training the script ends in (<c>set_shopkeep</c>, <c>0x80050a34</c>): the
    /// wanderer talked to, or with none, the speaker at the end.
    /// </summary>
    public RecordId? Keeper => With?.Party.Entity ?? Runner.Ending?.Speaker;

    /// <summary>Whether the script has ended, so the runtime should <see cref="End"/> it.</summary>
    public bool Ended => Runner.Status == ScriptStatus.Ended;

    /// <summary>
    /// Whether the script shows its page over a picture, a backdrop or the help menu, rather than in the
    /// conversation box. The world stops under it, as under the pause menu.
    /// </summary>
    public bool ShowsBackdrop => Runner.Script.Mode is DialogueMode.Backdrop or DialogueMode.Help;

    /// <summary>
    /// Whether trek's world is rebuilt when the script ends, as it is for a cut-scene, a backdrop or the
    /// help menu. The original frees the chunks and every wanderer as one starts (<c>clear_HUD_elements</c>,
    /// from <c>Trek_GameState</c> once <c>dialoug_func</c> asks for the dialog mode) and loads them again
    /// when it ends (<c>initGameTrek</c>), so every map object is gated again and every wanderer whose
    /// party is still there starts again at its home node. That's what takes a recruit away whose
    /// cut-scene added them. A conversation or a sign stays in trek, and nothing is gated again.
    /// </summary>
    public bool RebuildsTrek => Runner.Script.Mode is DialogueMode.Cutscene or DialogueMode.Backdrop or DialogueMode.Help;

    /// <summary>
    /// Talks to a wanderer (<c>monsterpary_func</c>, <c>monsterparty_wanderstruct</c>): its party's
    /// script starts, from reference point 0 and remembering by the party; the wanderer is held, both
    /// stand and turn to face each other, and the party's flag B is set. Null for a hunter or a silent
    /// party, which have no conversation.
    /// </summary>
    public static Conversation? TalkTo(GameState game, Wanderer wanderer, IScriptHost host, PlayerMotion player)
    {
        var party = wanderer.Party;
        if (party.Dialogue is not { } id || (party.Flags & PartyFlags.Silent) != 0 || game.Plugins.TryGet<DialogueRecord>(id) is not { } script)
            return null;
        var conversation = new Conversation(game, script, new ScriptStart(0, MapRecord.VoxelId(wanderer.Map.Record.Id, party.Index)), host,
            player, wanderer);
        conversation.Runner.Start();
        conversation.Hold();
        var body = wanderer.Body;
        var toBody = new Vector2(body.Sphere.Position.X - player.Sphere.Position.X, body.Sphere.Position.Z - player.Sphere.Position.Z);
        body.Face(-toBody);
        player.Face(toBody);
        if (party.Used is { } used)
            game.Flags.TrySet(used.Flag, true);
        return conversation;
    }

    /// <summary>
    /// Runs a script with no one to talk to, as a sign does (<c>dialouge_vobject_func</c> with a sign's
    /// script): the box shows, and the player stands.
    /// </summary>
    public static Conversation Show(GameState game, DialogueRecord script, ScriptStart start, IScriptHost host, PlayerMotion player)
    {
        var conversation = new Conversation(game, script, start, host, player, null);
        conversation.Runner.Start();
        player.Asked = Body.Idle;
        return conversation;
    }

    /// <summary>
    /// A button moving the conversation on (<c>run_dialougemode_funcs2</c>): both stand, the script
    /// moves on by <paramref name="advance"/>, and then, unless the script played an animation, the
    /// speaker of what's showing is asked to talk for a tick (<see cref="SpeakerTalk"/>).
    /// </summary>
    /// <param name="advance">What the button does: <see cref="ScriptRunner.Continue"/>, <see cref="ScriptRunner.Choose"/> and so on.</param>
    public void Press(Action advance)
    {
        Stand();
        if (!_host.Talk.Press(advance) || Runner.Page?.Line.Speaker is not { Type: RecordTypes.Entity } speaker)
            return;
        if (speaker == GameState.Alaron)
            _player.Asked = Body.Talk;
        else if (With is not null)
            With.Body.Request = Body.Talk;
    }

    /// <summary>
    /// What a conversation does each tick, after the bodies have moved (<c>run_dialougemode_funcs1</c>):
    /// the wanderer is held again and both stand, ending a talk's tick. A script's timer counts down.
    /// </summary>
    /// <returns>Whether the script moved on, its timer run out, so there's something new to show.</returns>
    public bool Tick()
    {
        var moved = false;
        if (Runner.Wait == ScriptWait.Timer)
        {
            Runner.Step(1);
            moved = Runner.Wait != ScriptWait.Timer;
        }
        if (With is not null)
        {
            Hold();
            Stand();
        }
        return moved;
    }

    /// <summary>
    /// Frames the two talking, each tick in place of the follow camera (<c>ProcessPlayers</c>). A sign
    /// leaves the camera be, so this does nothing.
    /// </summary>
    public void Frame(FollowCamera camera, CollisionWorld world)
    {
        if (With is not null)
            camera.Frame(world, _player.Sphere.Position, With.Body.Sphere.Position);
    }

    /// <summary>
    /// The end (<c>0x80051ccc</c> in 1.0): the camera goes back to the player, and the wanderer is let
    /// go and sets off for its node again, or if someone joined the party, its body goes. The runtime
    /// carries out a shop, training, battle or camp the script ended in.
    /// </summary>
    public void End(Wanderers wanderers, FollowCamera camera)
    {
        if (With is not null)
        {
            camera.Release(_player.Sphere.Position);
            With.Body.Held = false;
            if (Runner.Ending?.PartnerJoined == true)
                wanderers.Free(With);
            else
                With.Resume();
        }
        _player.Asked = null;
    }

    private void Hold()
    {
        With!.Calmed = true;
        With.Body.Held = true;
    }

    private void Stand()
    {
        _player.Asked = Body.Idle;
        if (With is not null)
            With.Body.Request = Body.Idle;
    }

    /// <summary>Passes the script's cues on, noting whether it played an animation (<c>ApplyStimulus</c>, <c>unkac</c>).</summary>
    private sealed class Watch(IScriptHost host) : IScriptHost
    {
        public SpeakerTalk Talk { get; } = new();

        public void Cue(ScriptCue cue)
        {
            if (cue is AnimateCue)
                Talk.Animated();
            host.Cue(cue);
        }
    }
}
