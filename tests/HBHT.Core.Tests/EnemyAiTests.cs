using HBHT.Core.Combat;
using HBHT.Core.Rng;

namespace HBHT.Core.Tests;

/// <summary>
/// The enemy AI's pieces that need no battle (M3 session 6), against the original's own results: its quicksort with the sort stream,
/// where a leg leaves the field (<c>EnemyAI.ClipToCircle</c>), and the flank (<c>Combatant.IsFlanking</c>). The values came from 1.0's
/// functions run in session 5's MIPS interpreter (the session's research, <c>m3-s6-vectors.json</c>).
/// </summary>
public sealed class EnemyAiTests
{
    /// <summary>Score lists, whether ascending, the sort stream in, the original indexes in the order sorted, and the stream after.</summary>
    private static readonly (int[] Scores, bool Ascending, uint Seed, int[] Order, uint SeedAfter)[] Sorts =
    [
        ([2, 1, 1, 1, 0, 2], false, 0x79EAA723u, [5, 0, 2, 3, 1, 4], 0xCBF1134Du),
        ([0, 2, 0, 3], false, 0x2C34BF1Cu, [3, 1, 2, 0], 0x28FDCFCBu),
        ([3, 1, 2, 2, 0, 0, 2, 3, 1, 1], false, 0x311275FCu, [7, 0, 6, 3, 2, 1, 9, 8, 5, 4], 0xBF008AF0u),
        ([1, 3, 0], false, 0x198F69A0u, [1, 0, 2], 0x198F69A0u),
        ([0, 0, 3, 0, 0, 2, 3], false, 0xA0879A14u, [6, 2, 5, 3, 1, 0, 4], 0x7C941066u),
        ([0, 3, 1, 1, 3, 3, 1], true, 0xC4BD1F80u, [0, 6, 3, 2, 4, 1, 5], 0x837BF4B2u),
        ([0, 0], false, 0x7EED8E76u, [0, 1], 0x7EED8E76u),
        ([3, 0, 2, 3, 0, 3, 0], true, 0x25BCA5D7u, [6, 4, 1, 2, 0, 5, 3], 0x6A62E30Cu),
        ([0, 0, 3, 2, 3, 0, 0, 3, 1, 0], false, 0xB374B7ECu, [7, 4, 2, 3, 8, 5, 0, 1, 6, 9], 0xDC9773E0u),
        ([0, 3, 0, 3, 3], false, 0x1A658833u, [4, 3, 1, 2, 0], 0x54D62FF6u),
        ([2, 1, 0, 1, 1], false, 0x262F2B99u, [0, 4, 3, 1, 2], 0x3A242024u),
        ([1, 3], true, 0xF788956Eu, [0, 1], 0xF788956Eu),
        ([2, 0, 3, 3, 2, 2, 2, 3, 1], true, 0x04AF68ABu, [1, 8, 5, 0, 4, 6, 2, 7, 3], 0x23BE9070u),
        ([0, 0, 1], false, 0x024226E3u, [2, 0, 1], 0x024226E3u),
        ([1, 0, 3], false, 0x2A544488u, [2, 0, 1], 0x2A544488u),
        ([2, 3, 3, 3, 1, 3, 2, 2], false, 0xDD42F005u, [5, 3, 2, 1, 6, 7, 0, 4], 0x138FDBD2u),
        ([2, 2, 1, 2, 2, 3, 3, 3, 2, 3], true, 0xF86E2C6Cu, [2, 3, 8, 4, 1, 0, 9, 7, 6, 5], 0x9681F060u),
        ([2, 0, 1], true, 0x23377B1Cu, [1, 2, 0], 0x23377B1Cu),
        ([1, 0], false, 0x8717EF6Du, [0, 1], 0x8717EF6Du),
        ([1, 2, 1], true, 0xA716D8C1u, [0, 2, 1], 0xA716D8C1u),
        ([3, 0], true, 0xE13698E6u, [1, 0], 0xE13698E6u),
        ([2, 3, 0], true, 0x0C128705u, [2, 0, 1], 0x0C128705u),
        ([3, 3, 0, 3], false, 0x012C3591u, [3, 1, 0, 2], 0x242E71BCu),
        ([3, 1], true, 0x09D16EB0u, [1, 0], 0x09D16EB0u),
    ];

    [Fact]
    public void TheQuicksortPutsEqualScoresInTheOriginalsOrderAndDrawsAsItDid()
    {
        foreach (var (scores, ascending, seed, order, after) in Sorts)
        {
            var list = new AiScores(scores.Length);
            for (var i = 0; i < scores.Length; i++)
                list.Add(i, 0, 0, scores[i], null);
            var random = new Lcg(seed);
            list.Sort(ascending, random);
            Assert.Equal(order, list.Entries.Select(e => e.X));
            Assert.Equal(after, random.Seed);
        }
    }

    [Fact]
    public void KeepBestKeepsTheEntriesTiedWithTheFirst()
    {
        var list = new AiScores(5);
        foreach (var score in (int[])[2, 2, 2, 3, 2])
            list.Add(0, 0, 0, score, null);
        list.KeepBest();
        Assert.Equal(3, list.Count);
        var one = new AiScores(3);
        foreach (var score in (int[])[1, 2, 2])
            one.Add(0, 0, 0, score, null);
        one.KeepBest();
        Assert.Equal(1, one.Count);
    }

    private static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);

    /// <summary>Centre, radius, the segment's ends, whether it meets the circle and where, as the floats' bits.</summary>
    private static readonly (uint Cx, uint Cz, uint R, uint Ax, uint Az, uint Bx, uint Bz, bool Hit, uint X, uint Z)[] Clips =
    [
        (0x41B00000u, 0x42300000u, 0x40400000u, 0x41B00000u, 0x42300000u, 0x40C00000u, 0x3F800000u, true, 0x41A7A161u, 0x4224C0DBu),
        (0x41380000u, 0x41C00000u, 0x40A00000u, 0x41000000u, 0x41E00000u, 0x40C00000u, 0x40400000u, true, 0x40FC5FBCu, 0x41D4AB2Au),
        (0x41EC0000u, 0x41400000u, 0x41000000u, 0x42100000u, 0x41200000u, 0x420C0000u, 0x421C0000u, true, 0x420F3F04u, 0x41777222u),
        (0x42390000u, 0x41000000u, 0x40E00000u, 0x42180000u, 0x40A00000u, 0x42540000u, 0x3F800000u, false, 0u, 0u),
        (0x42680000u, 0x41A00000u, 0x40400000u, 0x42680000u, 0x41A00000u, 0x41400000u, 0x42080000u, true, 0x425C8519u, 0x41A6FCE6u),
        (0x421A0000u, 0x426C0000u, 0x40E00000u, 0x421C0000u, 0x42580000u, 0x41200000u, 0x42200000u, true, 0x420D46DEu, 0x4250E46Bu),
        (0x42210000u, 0x421C0000u, 0x41400000u, 0x42300000u, 0x41F80000u, 0x42280000u, 0x41F00000u, false, 0u, 0u),
        (0x42160000u, 0x41C00000u, 0x40C00000u, 0x42100000u, 0x41C00000u, 0x41800000u, 0x41100000u, true, 0x41FE762Bu, 0x41A6D8A0u),
        (0x41000000u, 0x42300000u, 0x40A00000u, 0x41000000u, 0x42300000u, 0x42280000u, 0x425C0000u, true, 0x414C1D96u, 0x4236280Au),
        (0x42190000u, 0x41800000u, 0x41300000u, 0x421C0000u, 0x41980000u, 0x41100000u, 0x42180000u, true, 0x41F3DC95u, 0x41C32780u),
        (0x422C0000u, 0x423C0000u, 0x3F800000u, 0x42140000u, 0x423C0000u, 0x41D00000u, 0x41B80000u, false, 0u, 0u),
        (0x424A0000u, 0x41700000u, 0x40A00000u, 0x42680000u, 0x41B00000u, 0x41400000u, 0x421C0000u, false, 0u, 0u),
        (0x41400000u, 0x41F00000u, 0x40E00000u, 0x41400000u, 0x41F00000u, 0x42500000u, 0x422C0000u, true, 0x41954206u, 0x4200A787u),
        (0x42290000u, 0x42700000u, 0x41000000u, 0x42240000u, 0x42600000u, 0x41D00000u, 0x41880000u, true, 0x421CC467u, 0x424D31DAu),
        (0x420A0000u, 0x420C0000u, 0x40C00000u, 0x42240000u, 0x42040000u, 0x41800000u, 0x41400000u, false, 0u, 0u),
        (0x41140000u, 0x41F00000u, 0x40E00000u, 0x40C00000u, 0x41C80000u, 0x41900000u, 0x41C00000u, true, 0x413524E6u, 0x41C473CCu),
        (0x422C0000u, 0x41900000u, 0x41200000u, 0x422C0000u, 0x41900000u, 0x41800000u, 0x42340000u, true, 0x420FB73Au, 0x41C8918Cu),
        (0x41500000u, 0x42140000u, 0x40400000u, 0x41200000u, 0x41E80000u, 0x42300000u, 0x426C0000u, false, 0u, 0u),
        (0x41180000u, 0x41A00000u, 0x40A00000u, 0x40400000u, 0x41C00000u, 0x41700000u, 0x41C00000u, false, 0u, 0u),
        (0x42810000u, 0x41B80000u, 0x41100000u, 0x426C0000u, 0x41B80000u, 0x41600000u, 0x41A00000u, true, 0x42587B91u, 0x41B565CFu),
        (0x41E00000u, 0x42600000u, 0x41300000u, 0x41E00000u, 0x42600000u, 0x41A00000u, 0x42640000u, true, 0x4188ADF7u, 0x42657521u),
        (0x40900000u, 0x42200000u, 0x41000000u, 0x40E00000u, 0x42200000u, 0x41300000u, 0x41C80000u, true, 0x410E0786u, 0x4203D8F2u),
        (0x41820000u, 0x42280000u, 0x41000000u, 0x41800000u, 0x42400000u, 0x41200000u, 0x42040000u, true, 0x414C5965u, 0x421FB7DFu),
        (0x423C0000u, 0x42000000u, 0x40E00000u, 0x42580000u, 0x41F80000u, 0x42400000u, 0x41500000u, false, 0u, 0u),
        (0x41000000u, 0x41D00000u, 0x41200000u, 0x41000000u, 0x41D00000u, 0x42700000u, 0x421C0000u, true, 0x418D9C85u, 0x41E36721u),
        (0x3E800000u, 0x41700000u, 0x40400000u, 0x40800000u, 0x41400000u, 0x425C0000u, 0x41900000u, false, 0u, 0u),
        (0x42290000u, 0x41D80000u, 0x41200000u, 0x42140000u, 0x42080000u, 0x42700000u, 0x41700000u, true, 0x4234FCBEu, 0x41D97FD0u),
        (0x42310000u, 0x42300000u, 0x41200000u, 0x42140000u, 0x42440000u, 0x424C0000u, 0x41200000u, true, 0x4220DB8Eu, 0x42202EACu),
        (0x42580000u, 0x40400000u, 0x40400000u, 0x42580000u, 0x40400000u, 0x40E00000u, 0x40C00000u, true, 0x424C063Du, 0x404C3AFDu),
        (0x41740000u, 0x40000000u, 0x3F800000u, 0x41000000u, 0x40E00000u, 0x42600000u, 0x420C0000u, false, 0u, 0u),
        (0x40400000u, 0x424C0000u, 0x41200000u, 0x40A00000u, 0x42440000u, 0x41C00000u, 0x41800000u, true, 0x411271CAu, 0x4227262Bu),
        (0x427E0000u, 0x423C0000u, 0x41000000u, 0x42680000u, 0x42380000u, 0x42200000u, 0x41B80000u, true, 0x425E2178u, 0x422B63A7u),
        (0x41600000u, 0x40800000u, 0x40800000u, 0x41600000u, 0x40800000u, 0x41A00000u, 0x40400000u, true, 0x418F908Au, 0x4055E9F3u),
        (0x42420000u, 0x41B00000u, 0x41000000u, 0x42300000u, 0x41980000u, 0x42280000u, 0x42040000u, true, 0x422BF0CEu, 0x41D0D4B8u),
        (0x423A0000u, 0x41800000u, 0x41200000u, 0x42380000u, 0x41980000u, 0x41300000u, 0x41A00000u, true, 0x4213101Cu, 0x419A1C56u),
        (0x40600000u, 0x40800000u, 0x41000000u, 0x3F800000u, 0x40000000u, 0x421C0000u, 0x41B80000u, true, 0x410F1D06u, 0x40CC7E66u),
        (0x42400000u, 0x40E00000u, 0x41300000u, 0x42400000u, 0x40E00000u, 0x41D80000u, 0x40A00000u, true, 0x421432BDu, 0x40BEA090u),
        (0x41BC0000u, 0x420C0000u, 0x40400000u, 0x41900000u, 0x42200000u, 0x42340000u, 0x41A00000u, false, 0u, 0u),
        (0x426D0000u, 0x41900000u, 0x40E00000u, 0x426C0000u, 0x41C80000u, 0x41C00000u, 0x00000000u, true, 0x426BF278u, 0x41C7ECACu),
        (0x42560000u, 0x41100000u, 0x40000000u, 0x42580000u, 0x3F800000u, 0x42100000u, 0x420C0000u, false, 0u, 0u),
    ];

    [Fact]
    public void ALegLeavesTheFieldWhereTheOriginalsQuadraticSays()
    {
        foreach (var (cx, cz, r, ax, az, bx, bz, hit, x, z) in Clips)
        {
            var point = Battle.ClipToCircle(new(F(cx), F(cz)), F(r), new(F(ax), F(az)), new(F(bx), F(bz)));
            Assert.Equal(hit, point is not null);
            if (point is { } p)
                Assert.Equal((F(x), F(z)), (p.X, p.Y));
        }
    }

    /// <summary>The attacker's cell and direction, the target's cell and direction, and whether it's on the target's flank.</summary>
    private static readonly (int Ax, int Az, int Ad, int Tx, int Tz, int Td, bool Flanking)[] Flanks =
    [
        (30, 30, 5, 31, 28, 1, false),
        (30, 30, 0, 28, 33, 7, false),
        (30, 30, 0, 29, 29, 0, false),
        (30, 30, 1, 30, 31, 3, false),
        (30, 30, 4, 31, 28, 3, false),
        (30, 30, 1, 28, 29, 1, false),
        (30, 30, 5, 28, 27, 4, false),
        (30, 30, 6, 30, 28, 5, false),
        (30, 30, 7, 30, 29, 0, false),
        (30, 30, 3, 32, 28, 5, false),
        (30, 30, 4, 33, 28, 3, false),
        (30, 30, 0, 30, 33, 6, false),
        (30, 30, 6, 28, 33, 4, false),
        (30, 30, 6, 30, 32, 0, false),
        (30, 30, 7, 32, 29, 1, false),
        (30, 30, 3, 28, 33, 0, false),
        (30, 30, 0, 32, 28, 4, false),
        (30, 30, 6, 27, 29, 6, false),
        (30, 30, 3, 28, 32, 0, false),
        (30, 30, 5, 33, 29, 1, false),
        (30, 30, 1, 33, 30, 1, false),
        (30, 30, 3, 30, 28, 0, false),
        (30, 30, 3, 28, 31, 6, false),
        (30, 30, 0, 33, 30, 5, false),
        (30, 30, 7, 28, 32, 6, false),
        (30, 30, 7, 29, 28, 7, true),
        (30, 30, 5, 28, 29, 5, false),
        (30, 30, 7, 33, 27, 4, false),
        (30, 30, 7, 31, 30, 4, false),
        (30, 30, 1, 32, 32, 5, false),
        (30, 30, 4, 30, 27, 1, false),
        (30, 30, 7, 29, 29, 4, false),
        (30, 30, 4, 33, 29, 4, false),
        (30, 30, 7, 28, 31, 0, false),
        (30, 30, 1, 29, 33, 7, false),
        (30, 30, 0, 32, 33, 3, false),
        (30, 30, 6, 27, 30, 1, false),
        (30, 30, 5, 27, 33, 5, true),
        (30, 30, 3, 29, 27, 4, false),
        (30, 30, 5, 31, 31, 2, false),
        (30, 30, 0, 29, 30, 7, false),
        (30, 30, 6, 33, 32, 0, false),
        (30, 30, 4, 32, 31, 1, false),
        (30, 30, 2, 31, 30, 6, false),
        (30, 30, 1, 27, 32, 1, false),
        (30, 30, 7, 33, 30, 5, false),
        (30, 30, 7, 28, 30, 1, false),
        (30, 30, 4, 31, 33, 6, false),
        (30, 30, 3, 33, 29, 6, false),
        (30, 30, 2, 30, 31, 4, false),
        (30, 30, 5, 32, 32, 7, false),
        (30, 30, 1, 30, 27, 7, false),
        (30, 30, 1, 27, 27, 1, false),
        (30, 30, 0, 28, 29, 3, false),
        (30, 30, 4, 32, 30, 6, false),
        (30, 30, 3, 32, 32, 2, true),
        (30, 30, 1, 28, 27, 3, false),
        (30, 30, 4, 31, 29, 0, false),
        (30, 30, 2, 33, 29, 3, false),
        (30, 30, 6, 31, 28, 4, false),
    ];

    [Fact]
    public void TheFlankIsTheOriginals()
    {
        foreach (var (ax, az, ad, tx, tz, td, flanking) in Flanks)
            Assert.Equal(flanking, Battle.IsFlanking(ax, az, ad, tx, tz, td));
    }
}
