namespace HBHT.Rom.Engine;

/// <summary>Tables in the code segment about actors (Borg7s).</summary>
public static class ActorTables
{
    /// <summary>
    /// The actors whose bodies <c>ProcessPlayers</c> (<c>0x80012784</c> in 1.0) animates in combat even when they idle 13.0 or
    /// more from the camera's aim, as Borg7 indices: <c>Body.AlwaysAnimated</c> (<c>0x800CDA50</c>), a list of u16s ending with a
    /// 0, which <c>Body.InAlwaysAnimated</c> (<c>0x80011830</c>) searches by the actor id at body +0x0C. Nine: the bat, wyvern,
    /// harpy, the two dryads, Pandara, the gryphon, the manticore and the scorpion, which the decomp calls the flying models.
    /// </summary>
    public static IReadOnlyList<int> ReadAlwaysAnimated(RomImage rom) => Be.U16sUntilZero(rom.Table(CodeTable.AlwaysAnimated));
}
