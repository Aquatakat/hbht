using System.Globalization;
using Aidyn.Core.Characters;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

public class SpellOrderTests
{
    private static LearnedSpell Learn(int id, MagicSchool school, int? position) =>
        new(Characters.Spell(id, school, wizard: 1, cost: 100) with { Position = position }, 1);

    private static IEnumerable<int> Ids(IEnumerable<LearnedSpell> spells) =>
        spells.Select(s => int.Parse(s.Spell.Id.Key[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    [Fact]
    public void AStarCharacterKeepsTheUnschooledSpellsThenStarsInTheSpellTablesOrder()
    {
        LearnedSpell[] learnt =
        [
            Learn(0x0501, MagicSchool.Star, 20),
            Learn(0x0401, MagicSchool.None, 50),
            Learn(0x0502, MagicSchool.Star, 10),
            Learn(0x0402, MagicSchool.None, 5),
        ];

        Assert.Equal([0x0402, 0x0401, 0x0502, 0x0501], Ids(GameState.SpellOrder(learnt, MagicSchool.Star, alaron: false)));
    }

    [Fact]
    public void AChaosAlaronKeepsStarThenTheElementalNamingAndNecromancySpells()
    {
        LearnedSpell[] learnt =
        [
            Learn(0x0301, MagicSchool.Necromancy, 1),
            Learn(0x0201, MagicSchool.Naming, 1),
            Learn(0x0101, MagicSchool.Elemental, 9),
            Learn(0x0102, MagicSchool.Elemental, 3),
            Learn(0x0501, MagicSchool.Star, 7),
            Learn(0x0401, MagicSchool.None, 30),
        ];

        Assert.Equal([0x0401, 0x0501, 0x0102, 0x0101, 0x0201, 0x0301],
            Ids(GameState.SpellOrder(learnt, MagicSchool.Chaos, alaron: true)));
    }

    [Fact]
    public void ASpellWithNoPlaceInTheTableGoesAfterThoseWithOneInItsSchool()
    {
        LearnedSpell[] learnt =
        [
            Learn(0x0402, MagicSchool.None, null),
            Learn(0x0401, MagicSchool.None, 40),
            Learn(0x0502, MagicSchool.Star, null),
            Learn(0x0501, MagicSchool.Star, 2),
        ];

        Assert.Equal([0x0401, 0x0402, 0x0501, 0x0502], Ids(GameState.SpellOrder(learnt, MagicSchool.Star, alaron: false)));
    }

    [Fact]
    public void ASpellOfAnotherSchoolGoesLastWhereTheOriginalWouldDropIt()
    {
        LearnedSpell[] learnt =
        [
            Learn(0x0101, MagicSchool.Elemental, 1),
            Learn(0x0501, MagicSchool.Star, 2),
            Learn(0x0401, MagicSchool.None, 3),
            Learn(0x0301, MagicSchool.Necromancy, 4),
        ];

        // Not Alaron: the Elemental and Necromancy spells aren't his school's, but they stay, after the rest.
        Assert.Equal([0x0401, 0x0501, 0x0101, 0x0301], Ids(GameState.SpellOrder(learnt, MagicSchool.Chaos, alaron: false)));
        // A Necromancy character's own school comes after the unschooled spells, and the Star spell goes last.
        Assert.Equal([0x0401, 0x0301, 0x0101, 0x0501], Ids(GameState.SpellOrder(learnt, MagicSchool.Necromancy, alaron: false)));
    }
}
