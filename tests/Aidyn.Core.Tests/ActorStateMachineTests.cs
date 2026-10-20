using Aidyn.Core.Actors;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class ActorStateMachineTests
{
    private static readonly RecordId Scene = RecordId.Parse("aidyn:scene/0x29D0");

    /// <summary>
    /// A walker in the shape of Brenna's actor: idle (0) repeats; walking starts a walk (1), which
    /// steps (2) until walking stops. Request 7 interrupts idle. Every animation is 4 ticks.
    /// </summary>
    private static ActorStateMachine Walker() => new(new ActorRecord(RecordId.Parse("aidyn:actor/0x29E3"), Scene, 2, 0,
    [
        new(RecordId.Parse("aidyn:animation/0x29D2"), [new(2, 100, true, 1), new(7, 100, true, 3), new(0, 100, false, 0)]),
        new(RecordId.Parse("aidyn:animation/0x29D3"), [new(2, 100, false, 2), new(ActorStateMachine.AnyRequest, 100, false, 0)]),
        new(RecordId.Parse("aidyn:animation/0x29D4"), [new(2, 100, false, 2), new(ActorStateMachine.AnyRequest, 100, false, 0)]),
        new(RecordId.Parse("aidyn:animation/0x29D5"), [new(ActorStateMachine.AnyRequest, 100, false, 0)]),
    ]), _ => 4, new Lcg(0));

    [Fact]
    public void StartsInTheInitialStateAndRestartsItWithNoBranchTaken()
    {
        var actor = new ActorStateMachine(new ActorRecord(RecordId.Parse("aidyn:actor/0x0001"), Scene, 2, 1,
            [new(RecordId.Parse("aidyn:animation/0x0002"), []), new(RecordId.Parse("aidyn:animation/0x0003"), [])]), _ => 3, new Lcg(0));
        Assert.Equal((1, 1), (actor.State, actor.Clock));
        Assert.False(actor.Tick());
        Assert.False(actor.Tick());
        Assert.Equal(3, actor.Clock);
        Assert.True(actor.Tick());
        Assert.Equal((1, 1), (actor.State, actor.Clock));
    }

    [Fact]
    public void AStateTakesOnlyARequestItHasABranchFor()
    {
        // ActorStateMachine.SetRequest: request 7 still asked for in the state it entered (3, which has only "any") counts as none,
        // a change, so the tick after the interrupt doesn't advance; the next, with 7 let go, does.
        var actor = Walker();
        actor.Request = 7;
        Assert.True(actor.Tick());
        Assert.Equal((3, 1, 0), (actor.State, actor.Clock, actor.Taken));
        Assert.False(actor.Tick());
        Assert.Equal((3, 1), (actor.State, actor.Clock));
        actor.Request = 0;
        Assert.False(actor.Tick());
        Assert.Equal((3, 2), (actor.State, actor.Clock));
    }

    [Fact]
    public void BranchesAtTheEndOfTheAnimationByRequest()
    {
        var actor = Walker();
        for (var i = 0; i < 3; i++)
            actor.Tick();
        Assert.Equal((0, 4), (actor.State, actor.Clock));
        Assert.True(actor.Tick());
        Assert.Equal((0, 1), (actor.State, actor.Clock));

        // Walk interrupts idle, and doesn't advance the tick it's asked for.
        actor.Request = 2;
        Assert.True(actor.Tick());
        Assert.Equal((1, 1), (actor.State, actor.Clock));
        for (var i = 0; i < 3; i++)
            actor.Tick();
        Assert.True(actor.Tick());
        Assert.Equal(2, actor.State);

        // Stopping doesn't interrupt a step: it waits for the step's end, then takes "any".
        actor.Request = 0;
        Assert.False(actor.Tick());
        Assert.Equal((2, 1), (actor.State, actor.Clock));
        for (var i = 0; i < 3; i++)
            actor.Tick();
        Assert.True(actor.Tick());
        Assert.Equal(0, actor.State);
    }

    [Fact]
    public void AnInterruptingRequestCutsIn()
    {
        var actor = Walker();
        actor.Tick();
        actor.Request = 7;
        Assert.True(actor.Tick());
        Assert.Equal((3, 1), (actor.State, actor.Clock));
    }

    [Fact]
    public void AJoinEntersTheRequestsStatePartwayAndCountsOnFromThere()
    {
        var actor = Walker();
        actor.Join(7, 3);
        Assert.Equal((3, 3), (actor.State, actor.Clock));
        Assert.False(actor.Tick());
        Assert.Equal(4, actor.Clock);
        // A state with no branch for the request can't join it.
        Assert.Throws<ArgumentException>(() => actor.Join(2, 1));
        // Past the end, it starts at the last frame.
        actor.Enter(0);
        actor.Join(7, 9);
        Assert.Equal((3, 4), (actor.State, actor.Clock));
    }

    [Fact]
    public void TheRootDoesntMoveOnTheTickAStateIsEnteredOrTheTwoAfter()
    {
        var actor = Walker();
        actor.Request = 2;
        bool[] moves = [.. Enumerable.Range(0, 6).Select(_ => { actor.Tick(); return actor.RootMoves; })];
        Assert.Equal([false, false, false, true, false, false], moves);
    }

    /// <summary>
    /// A roll is <c>Range(0, 99)</c> on the stream given: from a stream at 0, the original's start, the first is
    /// (100 × 0x3C6EF35F) >> 32 = 23, which a first branch of chance 23 takes and one of 22 doesn't.
    /// </summary>
    [Theory]
    [InlineData(23, 1)]
    [InlineData(22, 2)]
    public void RollsAreRangesOfTheStream(int chance, int taken)
    {
        var record = new ActorRecord(RecordId.Parse("aidyn:actor/0x0001"), Scene, 2, 0,
        [
            new(RecordId.Parse("aidyn:animation/0x0002"), [new(ActorStateMachine.AnyRequest, chance, false, 1), new(ActorStateMachine.AnyRequest, 100, false, 2)]),
            new(RecordId.Parse("aidyn:animation/0x0003"), []),
            new(RecordId.Parse("aidyn:animation/0x0004"), []),
        ]);
        var rolls = new Lcg(0);
        var actor = new ActorStateMachine(record, _ => 1, rolls);
        Assert.True(actor.Tick());
        Assert.Equal(taken, actor.State);
        Assert.Equal(0x3C6EF35Fu, rolls.Seed);
    }

    [Fact]
    public void ChancesAreSubtractedInOrder()
    {
        // 30 then 70 of any: every roll takes one of the two, the first about 30% of the time.
        var record = new ActorRecord(RecordId.Parse("aidyn:actor/0x0001"), Scene, 2, 0,
        [
            new(RecordId.Parse("aidyn:animation/0x0002"), [new(ActorStateMachine.AnyRequest, 30, false, 1), new(ActorStateMachine.AnyRequest, 70, false, 2)]),
            new(RecordId.Parse("aidyn:animation/0x0003"), [new(ActorStateMachine.AnyRequest, 100, false, 0)]),
            new(RecordId.Parse("aidyn:animation/0x0004"), [new(ActorStateMachine.AnyRequest, 100, false, 0)]),
        ]);
        var actor = new ActorStateMachine(record, _ => 1, new Lcg(0));
        var counts = new int[3];
        for (var i = 0; i < 2000; i++)
        {
            actor.Tick();
            counts[actor.State]++;
            actor.Tick();
            Assert.Equal(0, actor.State);
        }
        Assert.Equal(0, counts[0]);
        Assert.InRange(counts[1], 500, 700);
        Assert.Equal(2000, counts[1] + counts[2]);
    }
}
