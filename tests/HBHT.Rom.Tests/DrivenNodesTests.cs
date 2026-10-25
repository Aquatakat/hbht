using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// What a scene's animations drive, with the nodes' rest values (<see cref="DrivenNodes"/>), which the views pose where Godot's glTF
/// loader leaves out a track held at rest. Phelan in the final cut-scene (<c>aidyn:dialogue/0x04FE</c>) is the case that showed it.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class DrivenNodesTests(Na10Import fixture)
{
    private static readonly RecordId Phelan = RecordId.Hex(RecordTypes.Actor, 0x2918);

    private PluginSet Plugins => fixture.Data.Set;

    private GlbReader Scene(RecordId actor) =>
        GlbReader.Parse(File.ReadAllBytes(Plugins.FindAsset(Plugins.Get<ActorRecord>(actor).Scene, "glb")!));

    private string StateAnimation(int state) =>
        SceneNames.Animation(Convert.ToInt32(Plugins.Get<ActorRecord>(Phelan).States[state].Animation.Key, 16));

    /// <summary>
    /// Phelan's lying states (9 to 13: requests 20, 21 and 9 and the death's last frame) hold his hips (transform 3) turned and his
    /// lost leg (transform 8) scaled to nothing at their rest values, which Godot drops; his idle (state 0) stands him on both legs.
    /// Posed at rest, his hips' pivot is 0.18 above his feet, where his idle's is 1.18: played after his idle with those tracks
    /// dropped, he stood at his hips' height, his blood about his knee.
    /// </summary>
    [Fact]
    public void Phelans_lying_states_hold_his_hips_and_lost_leg_at_rest()
    {
        using var file = Scene(Phelan);
        var driven = DrivenNodes.Read(file);
        var extras = SceneExtras.Read(file);
        var hipPivot = extras.Pivots[3];
        foreach (var state in new[] { 9, 10, 11, 12, 13 })
        {
            var nodes = driven.Of(StateAnimation(state));
            var hips = Assert.Single(nodes, n => n.Node == SceneNames.Transform(3));
            var leg = Assert.Single(nodes, n => n.Node == SceneNames.Transform(8));
            Assert.True(leg.Scale!.Value.Length() < 1e-6f);
            Assert.InRange(Hips(hips.Translation!.Value, hips.Rotation!.Value).Y, 0.1f, 0.25f);
            // Each is a track whose every key is the rest value: the kind Godot leaves out.
            Assert.True(HeldAtRest(file, StateAnimation(state), SceneNames.Transform(3), "rotation"));
            Assert.True(HeldAtRest(file, StateAnimation(state), SceneNames.Transform(8), "scale"));
        }
        // Standing, his hips are at a man's height and both legs show.
        Assert.False(HeldAtRest(file, StateAnimation(0), SceneNames.Transform(8), "scale"));
        var (t, r) = FirstSample(file, StateAnimation(0), SceneNames.Transform(3));
        Assert.InRange(Hips(t, r).Y, 1.1f, 1.25f);

        Vector3 Hips(Vector3 translation, Quaternion rotation) => translation + Vector3.Transform(hipPivot, rotation);
    }

    /// <summary>Whether the animation's track for the node's channel holds the node's rest value at every key.</summary>
    private static bool HeldAtRest(GlbReader file, string animation, string node, string path)
    {
        var (index, values) = Track(file, animation, node, path);
        var rest = file.Root.GetProperty("nodes")[index].TryGetProperty(path, out var r)
            ? r.EnumerateArray().Select(x => x.GetSingle()).ToArray()
            : path == "scale" ? [1f, 1, 1] : path == "rotation" ? [0f, 0, 0, 1] : [0f, 0, 0];
        return values.Select((v, i) => MathF.Abs(v - rest[i % rest.Length])).All(d => d < 1e-4f);
    }

    private static (Vector3 Translation, Quaternion Rotation) FirstSample(GlbReader file, string animation, string node)
    {
        var (_, t) = Track(file, animation, node, "translation");
        var (_, r) = Track(file, animation, node, "rotation");
        return (new Vector3(t[0], t[1], t[2]), new Quaternion(r[0], r[1], r[2], r[3]));
    }

    private static (int Node, float[] Values) Track(GlbReader file, string animation, string node, string path)
    {
        var root = file.Root;
        var nodes = root.GetProperty("nodes");
        var index = Enumerable.Range(0, nodes.GetArrayLength()).First(i => nodes[i].GetProperty("name").GetString() == node);
        var anim = root.GetProperty("animations").EnumerateArray().First(a => a.GetProperty("name").GetString() == animation);
        var samplers = anim.GetProperty("samplers");
        var channel = anim.GetProperty("channels").EnumerateArray().First(c =>
            c.GetProperty("target").GetProperty("node").GetInt32() == index && c.GetProperty("target").GetProperty("path").GetString() == path);
        return (index, file.Floats(samplers[channel.GetProperty("sampler").GetInt32()].GetProperty("output").GetInt32()));
    }
}
