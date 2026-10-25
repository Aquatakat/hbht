using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins;

namespace HBHT.Core.Scripts;

/// <summary>
/// The game state scripts read and change, apart from flags, which the runner reaches through the
/// flag network. The party (<see cref="Characters.Party"/>) answers the skill and stat questions, and
/// its inventory will answer the item ones with 2.3.
/// </summary>
/// <remarks>
/// Where the debug build crashes on a failed call (taking an item the party doesn't have, adding a
/// member to a full party, giving experience to someone not in it), retail does nothing, and so
/// does the runner.
/// </remarks>
public interface IScriptWorld
{
    bool InParty(RecordId entity);

    /// <summary>Carried or equipped.</summary>
    bool HasItem(RecordId item);

    /// <summary>
    /// The party's check of a skill, 0 to 21, rolled from <paramref name="random"/>
    /// (<c>Party::SkillCheck</c>; see <see cref="Characters.Party.SkillCheck"/>).
    /// </summary>
    int SkillCheck(int skill, IRandom random);

    int BestStat(int stat);

    int WorstStat(int stat);

    /// <summary>A party member's value of a stat, or 0 if <paramref name="member"/> isn't one.</summary>
    int MemberStat(RecordId? member, int stat);

    int PartySize { get; }

    /// <summary>
    /// The first party member's name as it stands, which a page shows where its text says
    /// <see cref="ScriptRunner.PlayerToken"/>; null with no one in the party.
    /// </summary>
    string? PlayerName { get; }

    uint Gold { get; set; }

    void AddItem(RecordId item);

    /// <summary>Takes one, from the inventory or equipment. False if the party has none.</summary>
    bool TakeItem(RecordId item);

    /// <summary>False if the party is full.</summary>
    bool AddMember(RecordId entity);

    /// <summary>Removes a living member without killing them.</summary>
    void RemoveMember(RecordId entity);

    /// <summary>False if <paramref name="member"/> isn't in the party.</summary>
    bool AddExp(RecordId member, int amount);

    /// <summary>Heals the party fully (<c>Party::CampHeal</c>). The hours pass when the script ends.</summary>
    void CampHeal();

    /// <summary>
    /// What the map object that started a script remembers from an earlier run, such as the
    /// party's skill level the last time a check was made (<see cref="MapObjectMemory"/>).
    /// Recalling a memory keeps it for longer.
    /// </summary>
    bool TryRecall(Memory memory, out int value);

    void Remember(Memory memory, int value);
}
