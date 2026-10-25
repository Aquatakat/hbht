namespace HBHT.Rom.Engine;

/// <summary>The camp ambush's factor for each terrain type (<c>camp_float_array</c>), read from the code segment.</summary>
public static class CampFactors
{
    public static float[] Read(RomImage rom) => Be.F32s(rom.Table(CodeTable.CampFactors));
}
