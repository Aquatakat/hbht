using System.Collections.Concurrent;

namespace HBHT.Plugins.Records;

/// <summary>
/// A school of magic (<c>aidyn:school/n</c>, ADR 0033), by the original's number: 0 Chaos, 1 Elemental, 2 Naming, 3 Necromancy,
/// 4 the unschooled and 5 Star. A spell is of one; a character learns spells of its own school and the unschooled
/// (<c>Entity::CanLearnSpell</c>).
/// </summary>
/// <param name="Icon">
/// Its icon beside a spell's in the spell lists and on the Party tab (<c>getSchoolIcon</c>, <c>Spell.SchoolIcons</c> at
/// <c>0x800DEFC0</c>). The unschooled have none.
/// </param>
/// <param name="LearnsEverySchool">A character of this school learns any school's spells: Chaos.</param>
/// <param name="Dispellable">
/// A dispel of this school takes its effects off (<c>Entity.Dispel</c>, <c>0x80071CD8</c>). The unschooled's effects can't be
/// dispelled.
/// </param>
public sealed record SchoolRecord(RecordId Id, string Name, RecordId? Icon = null, bool LearnsEverySchool = false, bool Dispellable = true);

/// <summary>
/// A damage and resistance element (<c>aidyn:element/n</c>, ADR 0033), by the original's number, 1 to 16 (0, none, is no record).
/// A weapon's damage and a spell's damage effects are of elements, and a resistance multiplies the damage of its element.
/// </summary>
/// <param name="Name">
/// What Sense Aura calls it (<c>String/Common</c>'s lines 241 to 257, which <c>0x800E0C00</c> points at and the element byte
/// indexes).
/// </param>
/// <param name="Covers">
/// The other elements a resistance to it meets on a weapon's blow (<c>Combatant.ApplyResistance</c>, <c>0x80062704</c>):
/// Physical covers Cutting and Smashing, and Magic every element but those three and Holy.
/// </param>
public sealed record ElementRecord(RecordId Id, string Name, IReadOnlyList<RecordId>? Covers = null)
{
    /// <summary>Whether a resistance to this element meets damage of <paramref name="element"/>: its own, or one it covers.</summary>
    public bool Meets(RecordId element) => element == Id || Covers?.Contains(element) == true;
}

/// <summary>
/// An animation request (<c>aidyn:request/n</c>, ADR 0033): the number the game asks a body's actor for, by the original's
/// number (0 idle, 2 walk, 5 cast and so on). An actor's states and branches say what each request plays, whether it loops and
/// whether it interrupts; the request says what the engine asks it for.
/// </summary>
/// <param name="Description">When the engine asks for it, or that none does.</param>
public sealed record RequestRecord(RecordId Id, string Name, string? Description = null);

/// <summary>
/// The numbers requests are played by (ADR 0004's handles): a base request's is its key, the original's number, and a mod's is
/// given one past the base's the first time it's asked for. Handles never leave the running process.
/// </summary>
public static class Requests
{
    /// <summary>What an actor's branch for any request has in the original: 255.</summary>
    public const int Any = 255;

    /// <summary>The first handle a mod's request gets.</summary>
    public const int FirstModHandle = 256;

    private static readonly ConcurrentDictionary<RecordId, int> ModHandles = new();
    private static readonly ConcurrentDictionary<int, RecordId> ModIds = new();
    private static int _next = FirstModHandle - 1;

    /// <summary>The base request <paramref name="number"/>: <c>aidyn:request/n</c>.</summary>
    public static RecordId Id(int number) =>
        number < FirstModHandle ? RecordId.Decimal(RecordTypes.Request, number)
            : ModIds.TryGetValue(number, out var id) ? id : throw new ArgumentOutOfRangeException(nameof(number), $"No request has handle {number}.");

    /// <summary>The number a request is played by.</summary>
    public static int Number(RecordId request)
    {
        if (request.Type != RecordTypes.Request)
            throw new ArgumentException($"{request} isn't a request.", nameof(request));
        if (request.Namespace == RecordId.BaseNamespace && int.TryParse(request.Key, out var n) && n < FirstModHandle)
            return n;
        return ModHandles.GetOrAdd(request, r =>
        {
            var handle = Interlocked.Increment(ref _next);
            ModIds[handle] = r;
            return handle;
        });
    }

    /// <summary>The number a request is played by, or <see cref="Any"/> for none.</summary>
    public static int NumberOrAny(RecordId? request) => request is { } r ? Number(r) : Any;
}
