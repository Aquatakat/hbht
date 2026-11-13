
using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The state the planner plans over: every plain flag, who's in the party in order, which of the items scripts and the model care
/// about the party holds, and whether the ending's credits have rolled or the game is over. Flags the engine writes (the calendar,
/// the rain, the terrain) are taken either way, as the pilot can wait or walk for them, and derived flags follow by their formulas.
/// Immutable: an action gives a new state.
/// </summary>
public sealed class PlanState : IEquatable<PlanState>
{
    private readonly QuestModel _model;
    private readonly ulong[] _bits;
    private readonly RecordId[] _party;
    private readonly RecordId[] _items;
    private byte[]? _values;
    private int? _hash;

    private PlanState(QuestModel model, ulong[] bits, RecordId[] party, RecordId[] items, bool credits, bool over, bool opening,
        RecordId? forced)
    {
        _model = model;
        _bits = bits;
        _party = party;
        _items = items;
        Credits = credits;
        Over = over;
        Opening = opening;
        Forced = forced;
    }

    /// <summary>A game's state as the planner sees it. A new game that hasn't played its opening has <see cref="Opening"/>.</summary>
    public static PlanState From(QuestModel model, GameState game)
    {
        var flags = model.Flags;
        var bits = new ulong[(flags.Count + 63) / 64];
        for (var h = 0; h < flags.Count; h++)
        {
            if (flags.Kind(h) == FlagKind.Plain && game.Flags.IsSetAsStored(flags.Id(h)))
                bits[h >> 6] |= 1UL << (h & 63);
        }
        var party = game.Party.Members.Select(m => m.Entity).ToArray();
        var items = model.TrackedItems.Where(game.Party.HasItem).OrderBy(i => i.ToString(), StringComparer.Ordinal).ToArray();
        var state = new PlanState(model, bits, party, items, false, false, false, null);
        var opening = game.StartsAsNewGame && model.Opening.Count > 0
            && model.Opening.Any(a => a.Effects.OfType<SetFlag>().FirstOrDefault() is { } first && !state.IsSet(first.Flag));
        return opening ? state.With(opening: true) : state;
    }

    public QuestModel Model => _model;

    /// <summary>Who's in the party, in order.</summary>
    public IReadOnlyList<RecordId> Party => _party;

    /// <summary>The items the model tracks (<see cref="QuestModel.TrackedItems"/>) that the party holds.</summary>
    public IReadOnlyList<RecordId> Items => _items;

    /// <summary>The ending's credits have rolled.</summary>
    public bool Credits { get; }

    /// <summary>The game is over.</summary>
    public bool Over { get; }

    /// <summary>A new game whose opening (<c>game.started</c>) is still to play: nothing else can be done first.</summary>
    public bool Opening { get; }

    /// <summary>A script object whose cut-scene a trigger has started: the next step is one of its ways, and nothing else.</summary>
    public RecordId? Forced { get; }

    /// <summary>What each flag can be, by handle (<see cref="FlagModel.CanBeClear"/>, <see cref="FlagModel.CanBeSet"/>).</summary>
    public byte Value(int handle) => Values[handle];

    private byte[] Values => _values ??= _model.Flags.Evaluate(h => Plain(h) ? FlagModel.CanBeSet : FlagModel.CanBeClear);

    /// <summary>A plain flag's value as stored.</summary>
    public bool Plain(int handle) => (_bits[handle >> 6] & (1UL << (handle & 63))) != 0;

    /// <summary>Whether a flag can be set: a plain flag that is, an engine flag, or a derived flag its inputs can make set.</summary>
    public bool IsSet(RecordId flag) => _model.Flags.TryHandle(flag, out var h) && (Values[h] & FlagModel.CanBeSet) != 0;

    /// <summary>Whether a flag can be clear.</summary>
    public bool IsClear(RecordId flag) => !_model.Flags.TryHandle(flag, out var h) || (Values[h] & FlagModel.CanBeClear) != 0;

    public bool InParty(RecordId entity) => Array.IndexOf(_party, entity) >= 0;

    public bool Holds(RecordId item) => Array.IndexOf(_items, item) >= 0;

    /// <summary>Whether a literal holds here. Code gates are taken to; places ask the model's <see cref="IPlaceReach"/>.</summary>
    public bool Holds(Literal literal) => literal switch
    {
        FlagIs f => f.Set ? IsSet(f.Flag) : IsClear(f.Flag),
        MemberIs m => InParty(m.Entity) == m.In,
        ItemIs i => Holds(i.Item) == i.Held,
        PartySizeIn p => _party.Length >= p.Min && _party.Length <= p.Max,
        Reaches r => _model.Reach.CanReach(r.Place, this),
        _ => true,
    };

    /// <summary>The first of an action's preconditions that doesn't hold here, or null if it can be done.</summary>
    public Literal? Blocker(PlayerAction action)
    {
        if (!Ready(action))
            return new CodeGate(Opening ? "the opening plays first" : $"{Forced} plays first");
        var forced = Forced is not null;
        foreach (var literal in forced ? action.Requires : action.Presence.Concat(action.Requires))
        {
            if (!Holds(literal))
                return literal;
        }
        return null;
    }

    /// <summary>Whether the action is the kind that can come next: the opening first, a started script's ways next.</summary>
    private bool Ready(PlayerAction action) =>
        Opening ? action.Kind == ActionKind.Event
        : Forced is { } forced ? action.Kind == ActionKind.Play && action.Place == forced
        : action.Kind != ActionKind.Event;

    /// <summary>Whether the action can be done here, and isn't the game's end.</summary>
    public bool CanDo(PlayerAction action) => !Over && Blocker(action) is null && !action.Effects.Any(e => e is EndGame);

    /// <summary>The state after an action's effects, in order.</summary>
    public PlanState Apply(PlayerAction action)
    {
        var bits = (ulong[])_bits.Clone();
        var party = _party.ToList();
        var items = _items.ToList();
        var credits = Credits;
        var over = Over;
        RecordId? forced = null;
        var flags = _model.Flags;

        void Write(RecordId? flag, bool value)
        {
            if (flag is not { } id || !flags.TryHandle(id, out var h) || flags.Kind(h) != FlagKind.Plain)
                return;
            if (value)
                bits[h >> 6] |= 1UL << (h & 63);
            else
                bits[h >> 6] &= ~(1UL << (h & 63));
        }

        foreach (var effect in action.Effects)
        {
            switch (effect)
            {
                case SetFlag set:
                    Write(set.Flag, set.Value);
                    break;
                case Join join:
                    if (!party.Contains(join.Entity))
                    {
                        if (party.Count >= HBHT.Core.Characters.Party.MaxSize)
                            break;
                        party.Add(join.Entity);
                    }
                    if (_model.MemberFlags(join.Entity) is { } joined)
                    {
                        Write(joined.Joined, true);
                        Write(joined.Left, false);
                    }
                    break;
                case Leave leave:
                    if (!party.Remove(leave.Entity))
                        break;
                    if (_model.MemberFlags(leave.Entity) is { } left)
                    {
                        Write(left.Joined, false);
                        Write(left.Left, true);
                    }
                    break;
                case Gain gain:
                    if (_model.IsTracked(gain.Item) && !items.Contains(gain.Item))
                        items.Add(gain.Item);
                    if (_model.KeyItem(gain.Item) is { } key)
                    {
                        Write(key.Held, true);
                        Write(key.Region, true);
                    }
                    break;
                case Lose lose:
                    items.Remove(lose.Item);
                    if (_model.KeyItem(lose.Item) is { } lost)
                        Write(lost.Held, false);
                    break;
                case RollCredits:
                    credits = true;
                    break;
                case EndGame:
                    over = true;
                    break;
                case StartScript start:
                    forced = start.MapObject;
                    break;
            }
        }
        return new PlanState(_model, bits, [.. party], [.. items.OrderBy(i => i.ToString(), StringComparer.Ordinal)], credits, over,
            Opening && action.Kind != ActionKind.Event, forced);
    }

    /// <summary>A copy with a flag written, for tests and tools.</summary>
    public PlanState With(RecordId flag, bool value)
    {
        var bits = (ulong[])_bits.Clone();
        var h = _model.Flags.Handle(flag);
        if (value)
            bits[h >> 6] |= 1UL << (h & 63);
        else
            bits[h >> 6] &= ~(1UL << (h & 63));
        return new PlanState(_model, bits, _party, _items, Credits, Over, Opening, Forced);
    }

    /// <summary>A copy with someone out of the party and their flags as they were, for tools (<see cref="PartyNeeds"/>).</summary>
    public PlanState Without(RecordId entity) =>
        new(_model, _bits, [.. _party.Where(p => p != entity)], _items, Credits, Over, Opening, Forced);

    /// <summary>
    /// A copy with someone in the party, their joined flag set and their left flag clear as a join writes them, for tools
    /// (<see cref="PartyNeeds"/>). The party may go past its cap: the relaxation keeps none.
    /// </summary>
    public PlanState Joined(RecordId entity) => Moved(entity, true);

    /// <summary>A copy with someone out of the party, their joined flag clear and their left flag set as a leave writes them, for tools.</summary>
    public PlanState Left(RecordId entity) => Moved(entity, false);

    private PlanState Moved(RecordId entity, bool joined)
    {
        var bits = (ulong[])_bits.Clone();
        void Write(RecordId? flag, bool value)
        {
            if (flag is not { } id || !_model.Flags.TryHandle(id, out var h) || _model.Flags.Kind(h) != FlagKind.Plain)
                return;
            if (value)
                bits[h >> 6] |= 1UL << (h & 63);
            else
                bits[h >> 6] &= ~(1UL << (h & 63));
        }
        if (_model.MemberFlags(entity) is { } flags)
        {
            Write(flags.Joined, joined);
            Write(flags.Left, !joined);
        }
        RecordId[] party = joined ? InParty(entity) ? _party : [.. _party, entity] : [.. _party.Where(p => p != entity)];
        return new PlanState(_model, bits, party, _items, Credits, Over, Opening, Forced);
    }

    private PlanState With(bool opening) => new(_model, _bits, _party, _items, Credits, Over, opening, Forced);

    public bool Equals(PlanState? other) => other is not null && (ReferenceEquals(this, other) ||
        GetHashCode() == other.GetHashCode() && _bits.AsSpan().SequenceEqual(other._bits) && _party.AsSpan().SequenceEqual(other._party)
        && _items.AsSpan().SequenceEqual(other._items) && Credits == other.Credits && Over == other.Over && Opening == other.Opening
        && Forced == other.Forced);

    public override bool Equals(object? obj) => Equals(obj as PlanState);

    public override int GetHashCode()
    {
        if (_hash is { } h)
            return h;
        var hash = new HashCode();
        foreach (var b in _bits)
            hash.Add(b);
        foreach (var p in _party)
            hash.Add(p);
        foreach (var i in _items)
            hash.Add(i);
        hash.Add(Credits);
        hash.Add(Over);
        hash.Add(Opening);
        hash.Add(Forced);
        _hash = hash.ToHashCode();
        return _hash.Value;
    }
}
