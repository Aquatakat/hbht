namespace Aidyn.Rom.Db;

/// <summary>How a flag's value is produced (the decomp's <c>event_flag_typeA</c>).</summary>
public enum FlagKind : byte
{
    /// <summary>A logic gate over its inputs.</summary>
    Logic,
    /// <summary>A bitmask of its inputs, compared with the operand.</summary>
    Value,
    /// <summary>A count of its set inputs, compared with the operand.</summary>
    Count,
    /// <summary>A plain bit, set directly by the game.</summary>
    Bit,
    Invalid,
}

/// <summary>A derived flag's operation (the decomp's <c>Event_flag_typeB</c>).</summary>
public enum FlagOp : byte
{
    And,
    Or,
    Xor,
    Not,
    Mask,
    Equal,
    Greater,
    Less,
    NotEqual,
    Invalid,
}

/// <summary>One event flag's definition (a <c>Struct_State</c>, 8 bytes).</summary>
/// <param name="Operand">The constant that value and count flags compare against.</param>
/// <param name="InputStart">Where this flag's inputs start in <see cref="GameStateFunnelTable.Inputs"/>.</param>
/// <param name="InitialValue">The value the flag starts a new game with.</param>
public sealed record FlagDefinition(
    ushort Operand,
    ushort InputStart,
    FlagKind Kind,
    FlagOp Op,
    byte InitialValue,
    byte InputCount);

/// <summary>
/// The raw <c>GameStateFunnel</c>: every event flag's definition and the shared input list
/// that derived flags read. Evaluating it is roadmap task 2.1.
/// </summary>
/// <param name="BitFlagCount">Flags below this index are plain bits the game sets directly.</param>
/// <param name="Unk06">Zero in every known ROM.</param>
/// <param name="Unk1C">A nonzero word the game doesn't use.</param>
/// <param name="Inputs">Flag indices. 0xFFFF marks an unused input.</param>
public sealed record GameStateFunnelTable(
    ushort BitFlagCount,
    ushort Unk06,
    uint Unk1C,
    IReadOnlyList<FlagDefinition> Flags,
    IReadOnlyList<ushort> Inputs)
{
    private const int HeaderSize = 0x20;
    private const int FlagSize = 8;

    internal static GameStateFunnelTable Read(ReadOnlySpan<byte> data)
    {
        var r = new RecordReader(data);
        var flagCount = r.U16Be();
        var inputCount = r.U16Be();
        var bitFlagCount = r.U16Be();
        var unk06 = r.U16Be();
        var flagsOffset = r.U32Be();
        var inputsOffset = r.U32Be();
        for (var i = 0; i < 3; i++)
        {
            if (r.U32Be() != 0)
                throw new InvalidDataException($"GameStateFunnel header word {0x10 + i * 4:X2} isn't zero.");
        }
        var unk1C = r.U32Be();

        if (flagsOffset != HeaderSize || inputsOffset != HeaderSize + flagCount * FlagSize)
            throw new InvalidDataException(
                $"GameStateFunnel offsets 0x{flagsOffset:X}/0x{inputsOffset:X} don't follow its {flagCount} flags.");
        if (bitFlagCount > flagCount)
            throw new InvalidDataException($"GameStateFunnel has {bitFlagCount} bit flags but only {flagCount} flags.");

        var flags = new FlagDefinition[flagCount];
        for (var i = 0; i < flags.Length; i++)
            flags[i] = new(r.U16Be(), r.U16Be(), (FlagKind)r.U8(), (FlagOp)r.U8(), r.U8(), r.U8());

        var inputs = new ushort[inputCount];
        for (var i = 0; i < inputs.Length; i++)
            inputs[i] = r.U16Be();

        return new GameStateFunnelTable(bitFlagCount, unk06, unk1C, flags, inputs);
    }
}
