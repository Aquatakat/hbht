// Prints how much of the code segment analysis covered.
// @category Aidyn

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.MemoryBlock;

public class Probe extends GhidraScript {
    @Override
    protected void run() throws Exception {
        MemoryBlock code = currentProgram.getMemory().getBlock("code");
        Listing listing = currentProgram.getListing();
        long insns = 0, last = 0;
        for (Instruction i : listing.getInstructions(code.getStart(), true)) {
            if (!code.contains(i.getAddress())) break;
            insns++;
            last = i.getAddress().getOffset();
        }
        int functions = currentProgram.getFunctionManager().getFunctionCount();
        long data = 0;
        for (Data d : listing.getDefinedData(code.getStart(), true)) {
            if (!code.contains(d.getAddress())) break;
            data++;
        }
        // Words that decode as jal into the code block, and how many of those targets are functions.
        long jals = 0, jalNotFunction = 0;
        Address a = code.getStart();
        while (a.compareTo(code.getEnd()) < 0) {
            int w = getInt(a);
            if ((w >>> 26) == 3) {
                long target = (a.getOffset() & 0xF0000000L) | ((w & 0x3FFFFFFL) << 2);
                Address t = toAddr(target);
                if (code.contains(t) && listing.getInstructionAt(a) != null) {
                    jals++;
                    if (currentProgram.getFunctionManager().getFunctionAt(t) == null) jalNotFunction++;
                }
            }
            a = a.add(4);
        }
        println(String.format("functions %d, instructions %d (%.1f%% of block), last instruction 0x%08x, defined data %d, disassembled jals %d, to non-functions %d",
            functions, insns, 100.0 * insns * 4 / code.getSize(), last, data, jals, jalNotFunction));
    }
}
