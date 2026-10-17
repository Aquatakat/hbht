using System.Numerics;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Lights;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class LightTests
{
    private static readonly Rgba Red = new(255, 0, 0, 255), Blue = new(0, 0, 255, 255), White = new(200, 200, 200, 255);

    private static PointLight Light(LightKind kind, float speed, Rgba? from = null, Rgba? to = null) =>
        new(Vector3.Zero, 5, kind, speed, from ?? Red, to ?? Blue);

    [Fact]
    public void AMapLightSitsAtItsChunksOriginPlusItsPosition()
    {
        var o = new LightObject(0, new Vector3(1, 2, 3), 5, PakVariant.Any, null, Red, Blue, LightKind.Static, 0, true, true);
        var light = PointLight.For(o, new Vector3(100, 0, 200));
        Assert.Equal(new Vector3(101, 2, 203), light.Position);
        Assert.True(light.Occludable);
        Assert.True(light.FacesCamera);
    }

    [Fact]
    public void BlendTruncatesTowardTheFirstColour()
    {
        // 255 + (0 − 255) × 0.5 = 127.5, truncated toward zero from the first: 255 − 127 = 128.
        Assert.Equal(new Rgba(128, 0, 127, 255), PointLight.Blend(Red, Blue, 0.5f));
        Assert.Equal(Blue, PointLight.Blend(Red, Blue, 2));
        Assert.Equal(Red, PointLight.Blend(Red, Blue, -1));
    }

    [Fact]
    public void StaticStaysAtItsFirstColour()
    {
        var light = Light(LightKind.Static, 0);
        light.Step(2, new ScriptedRandom());
        Assert.Equal(Red, light.Working);
    }

    [Fact]
    public void AlternateFadesOverSpeedTimes30TicksThenTurnsBack()
    {
        var light = Light(LightKind.Alternate, 1);
        for (var i = 0; i < 15; i++)
            light.Step(1, new ScriptedRandom());
        Assert.Equal(0.5f, light.Phase, 4);
        Assert.Equal(new Rgba(128, 0, 127, 255), light.Working);
        // Past the end, the colours change places, so it fades back from blue.
        for (var i = 0; i < 18; i++)
            light.Step(1, new ScriptedRandom());
        Assert.Equal(0.1f, light.Phase, 3);
        Assert.True(light.Working.B > 200 && light.Working.R < 50);
    }

    [Fact]
    public void SineCyclesOverSpeedTimes60TicksFromHalfWay()
    {
        var light = Light(LightKind.Sine, 0.5f);
        // A cycle is 30 ticks, 12° a tick. Half a cycle in, sin 180° = 0: half way again.
        for (var i = 0; i < 15; i++)
            light.Step(1, new ScriptedRandom());
        Assert.Equal(180, light.Phase, 2);
        Assert.Equal(new Rgba(128, 0, 127, 255), light.Working);
        light = Light(LightKind.Sine, 0.5f);
        light.Step(7, new ScriptedRandom());
        light.Step(1, new ScriptedRandom());
        Assert.Equal(96, light.Phase, 2);
        Assert.InRange(light.Working.B, 253, 255);
    }

    [Fact]
    public void RandomDriftsTowardItsTargetAndDrawsAnotherOnReachingIt()
    {
        var light = Light(LightKind.Random, 1);
        // Starting on its target (0), the first step reaches it and draws the next: half way.
        light.Step(1, new ScriptedRandom(0x8000_0000));
        Assert.Equal(0, light.Phase);
        Assert.Equal(0.5f, light.Target);
        light.Step(6, new ScriptedRandom());
        Assert.Equal(0.1f, light.Phase, 4);
    }

    [Fact]
    public void AFullSceneReplacesItsDimmestLightButNeverTheSun()
    {
        var scene = new SceneLights(SceneLights.ActorMax);
        scene.Add(new Rgba(1, 1, 1, 0), Vector3.UnitY);
        scene.Kept = 1;
        scene.Add(new Rgba(50, 0, 0, 0), Vector3.UnitX);
        // Brighter than the 50: it takes its place. Dimmer than what's there: dropped.
        scene.Add(new Rgba(0, 60, 0, 0), Vector3.UnitZ);
        scene.Add(new Rgba(0, 0, 40, 0), -Vector3.UnitZ);
        var lights = scene.After(scene.Kept);
        Assert.Equal([new DirectionalLight(new Vector3(0, 60, 0) / 255, Vector3.UnitZ)], lights);
    }

    [Fact]
    public void FalloffIsLinearToTheRadiusAndTheLightShinesFromWhereItIs()
    {
        var lights = new WorldLights(new ScriptedRandom()) { Sun = false };
        lights.BeginFrame(1);
        var light = new PointLight(new Vector3(0, 0, 4), 5, LightKind.Static, 0, White, White);
        var scene = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(scene, Vector3.Zero, [(light, true)]);
        // 4 of 5 away: a fifth of its colour, truncated.
        Assert.Equal([new DirectionalLight(new Vector3(40) / 255, Vector3.UnitZ)], scene.After(0));

        var far = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(far, new Vector3(0, 0, -1), [(light, true)]);
        lights.AddNear(far, Vector3.Zero, [(light, false)]);
        Assert.Equal(0, far.Count);
    }

    [Fact]
    public void ALightThatFacesTheCameraShinesFromTheEye()
    {
        var lights = new WorldLights(new ScriptedRandom()) { Eye = new Vector3(-10, 0, 0) };
        lights.BeginFrame(1);
        var scene = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(scene, Vector3.Zero, [(new PointLight(new Vector3(0, 0, 1), 5, LightKind.Static, 0, White, White, facesCamera: true), true)]);
        Assert.Equal(-Vector3.UnitX, scene.After(0)[0].Toward);
    }

    /// <summary>
    /// The colour is worked out once a frame, with the first reach's falloff, in place; a second in the frame takes
    /// it as it stands, whatever its distance, and the fade scales it again each time.
    /// </summary>
    [Fact]
    public void TheFirstReachInAFrameSetsTheFalloffAndTheFadeScalesEachTime()
    {
        var lights = new WorldLights(new ScriptedRandom()) { Fade = 0.5f };
        var light = new PointLight(Vector3.Zero, 10, LightKind.Static, 0, White, White);
        lights.BeginFrame(1);
        var near = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(near, new Vector3(0, 0, 5), [(light, true)]);
        var far = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(far, new Vector3(0, 0, 9), [(light, true)]);
        // 200 × 0.5 = 100, then × 0.5 = 50; the second takes that × 0.5 = 25, not its own 200 × 0.1.
        Assert.Equal(new Vector3(50) / 255, near.After(0)[0].Colour);
        Assert.Equal(new Vector3(25) / 255, far.After(0)[0].Colour);
        // The next frame works it out afresh.
        lights.BeginFrame(1);
        var again = new SceneLights(SceneLights.BodyMax);
        lights.AddNear(again, new Vector3(0, 0, 9), [(light, true)]);
        Assert.Equal(new Vector3(10) / 255, again.After(0)[0].Colour);
    }

    [Fact]
    public void ABodyOfThePartyGetsTheLightsItsGridCellListsAndOthersOnlyDynamicOnes()
    {
        var near = new LightObject(1, new Vector3(2, 1, 2), 5, PakVariant.Any, null, White, White, LightKind.Static, 0, false);
        var unlisted = new LightObject(2, new Vector3(3, 1, 2), 5, PakVariant.Any, null, White, White, LightKind.Static, 0, false);
        var grid = new ObjectGrid(Vector2.Zero, new Vector2(10, 10), 1, 1, [[1]]);
        var record = new MapRecord(RecordId.Hex(RecordTypes.Map, 1), [near, unlisted], grid);
        var map = LoadedMap.Activate(record, "chunk", ItemData.Flags(), new MapObjectMemory(), new CharacterParty(), new Lcg(), new Vector3(100, 0, 0));
        var lights = new WorldLights(new Lcg()) { Sun = true };
        lights.Load(map);
        lights.BeginFrame(1);
        var body = new Vector3(102, 1, 0);
        Assert.Single(lights.ForBody(body, map, party: true));
        Assert.Empty(lights.ForBody(body, map, party: false));
        // An actor gets every light of its chunk, but with the sun only room for one.
        lights.BeginFrame(1);
        Assert.Single(lights.ForActor(body, map));
        lights.Sun = false;
        lights.BeginFrame(1);
        Assert.Equal(2, lights.ForActor(body, map).Count);

        lights.Dynamic.Explosion(body);
        lights.BeginFrame(1);
        Assert.NotEmpty(lights.ForBody(body, map, party: false));
    }

    [Fact]
    public void DynamicLightsGoOutAtTheEndOfTheirLivesAndANewOneTakesTheSlotNumberedByTheCount()
    {
        var dynamic = new DynamicLights();
        dynamic.Explosion(Vector3.Zero);
        Assert.Equal(5, dynamic.Count);
        dynamic.Step(60);
        // The three short fires are out; the long fire and the burst are lit.
        Assert.Equal(2, dynamic.Count);
        var next = dynamic.Add(10, Vector3.One, 1, LightKind.Static, 0, White, White);
        // It goes in slot 2, which was free; one more would go in slot 3, also free, while slot 4 holds the burst.
        Assert.Equal(3, dynamic.Lit.Count());
        dynamic.Step(60);
        // The burst and the new light are out.
        Assert.Equal(1, dynamic.Count);
        Assert.DoesNotContain(next, dynamic.Lit);
        dynamic.Step(480);
        Assert.Equal(0, dynamic.Count);
    }

    [Fact]
    public void TheBurstFadesFromOrangeToNothingOverItsLife()
    {
        var burst = new DynamicLights();
        burst.Explosion(Vector3.Zero);
        var light = burst.Lit.Last();
        for (var i = 0; i < 119; i++)
            light.Step(1, new ScriptedRandom());
        Assert.True(light.Working.R < 5 && light.Working.G < 3);
    }
}
