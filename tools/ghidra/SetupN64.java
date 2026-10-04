// Runs before analysis, on a code segment imported raw at 0x80000400 (tools/ghidra.ps1 import).
// Maps the BSS the boot code clears, low memory and the hardware registers libultra touches, so their
// references resolve, and starts disassembly at the entry point.
// @category Aidyn

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.mem.Memory;
import ghidra.program.model.symbol.SourceType;

public class SetupN64 extends GhidraScript {
    @Override
    protected void run() throws Exception {
        Memory memory = currentProgram.getMemory();
        Address entry = toAddr(0x80000400L);

        // The entry point clears BSS: lui t0/addiu t0 is its start, lui t1/addiu t1 its end.
        long start = hiLo(entry.add(0x08), entry.add(0x0c));
        long end = hiLo(entry.add(0x10), entry.add(0x14));
        println(String.format("BSS 0x%08x-0x%08x", start, end));

        uninitialized("lowmem", 0x80000000L, 0x400);
        uninitialized("bss", start, end - start);
        // The rest of RDRAM (heap, frame buffers), with the Expansion Pak.
        uninitialized("heap", end, 0x80800000L - end);
        uninitialized("rsp", 0xA4000000L, 0x2000);       // SP DMEM and IMEM
        uninitialized("hwregs", 0xA4040000L, 0x800000);  // SP, DP, MI, VI, AI, PI, RI, SI registers
        uninitialized("pif", 0xBFC00000L, 0x800);

        createLabel(entry, "entry", true, SourceType.IMPORTED);
        disassemble(entry);
        createFunction(entry, "entry");
    }

    private long hiLo(Address lui, Address addiu) throws Exception {
        int hi = getInt(lui) & 0xffff;
        short lo = (short) (getInt(addiu) & 0xffff);
        return ((long) hi << 16) + lo & 0xffffffffL;
    }

    private void uninitialized(String name, long address, long length) throws Exception {
        currentProgram.getMemory().createUninitializedBlock(name, toAddr(address), length, false)
            .setPermissions(true, true, false);
    }
}
