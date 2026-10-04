// Runs after analysis. Analysis follows calls from the entry point, so it misses functions reached
// only through pointers: vtables, callbacks, jump tables of handlers. This makes a function at every
// undisassembled address in the text that something points at, either a word in the data that
// follows the text or a reference analysis found (a lui/addiu pair), and repeats until nothing new
// turns up. Gaps nothing points at stay undisassembled: the RSP microcode near the start of the
// segment is one.
// @category Aidyn

import java.util.*;

import ghidra.app.cmd.function.CreateFunctionCmd;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.MemoryBlock;

public class FindPointedFunctions extends GhidraScript {
    @Override
    protected void run() throws Exception {
        MemoryBlock code = currentProgram.getMemory().getBlock("code");
        Listing listing = currentProgram.getListing();
        Address textEnd = textEnd(code);
        println("text ends at " + textEnd);

        int made = 0;
        for (int pass = 1; ; pass++) {
            Set<Address> pointed = new TreeSet<>();
            // Words in the data after the text.
            for (Address a = textEnd; a.compareTo(code.getEnd()) < 0; a = a.add(4)) {
                long w = getInt(a) & 0xffffffffL;
                if (w >= 0x80000400L && w < textEnd.getOffset() && (w & 3) == 0) pointed.add(toAddr(w));
            }
            // References analysis made from instructions.
            for (Address a : currentProgram.getReferenceManager().getReferenceDestinationIterator(new AddressSet(code.getStart(), textEnd), true)) {
                if (a.getOffset() % 4 == 0) pointed.add(a);
            }
            int before = made;
            for (Address a : pointed) {
                if (a.compareTo(textEnd) >= 0) continue;
                if (listing.getInstructionContaining(a) != null || listing.getDefinedDataContaining(a) != null && listing.getDefinedDataContaining(a).isDefined()) continue;
                if (getInt(a) == 0) continue;
                if (disassemble(a) && new CreateFunctionCmd(a).applyTo(currentProgram, monitor)) made++;
            }
            println(String.format("pass %d: %d functions made", pass, made - before));
            if (made == before) break;
            analyzeChanges(currentProgram);
        }
        // What's left is code nothing points at (dead code the linker kept, or code only the
        // debug build calls) and the RSP microcode. Make a function of each run that disassembles
        // cleanly, returns, and calls only CPU code; bookmark it as unreferenced.
        int dead = 0;
        for (Address a = code.getStart(); a.compareTo(textEnd) < 0; a = a.add(4)) {
            if (listing.getInstructionContaining(a) != null || getInt(a) == 0) continue;
            Address runEnd = a;
            while (runEnd.compareTo(textEnd) < 0 && listing.getInstructionContaining(runEnd) == null) runEnd = runEnd.add(4);
            AddressSet run = new AddressSet(a, runEnd.subtract(1));
            if (disassemble(a) && cleanCode(run) && new CreateFunctionCmd(a).applyTo(currentProgram, monitor)) {
                createBookmark(a, "Aidyn", "Unreferenced: nothing in the segment calls or points at it");
                dead++;
                a = getFunctionAt(a).getBody().getMaxAddress().subtract(3);
            } else {
                clearListing(run);
                a = runEnd.subtract(4);
            }
        }
        println(dead + " unreferenced functions made");
        analyzeChanges(currentProgram);
        println("functions now " + currentProgram.getFunctionManager().getFunctionCount());
    }

    // Whether what disassembled from a gap stays in it, reaches a jr ra, and calls only into the code.
    private boolean cleanCode(AddressSet run) {
        boolean returns = false;
        for (Instruction i : currentProgram.getListing().getInstructions(run, true)) {
            String m = i.getMnemonicString();
            if (m.equals("jr") && i.toString().contains("ra")) returns = true;
            if (m.equals("jal") || m.equals("j")) {
                for (Address t : i.getFlows()) if (t.getOffset() < 0x80000400L || t.getOffset() >= 0x80800000L) return false;
            }
            if (m.startsWith("mtc0") || m.startsWith("mfc0") || m.equals("break")) return false;
        }
        Instruction last = currentProgram.getListing().getInstructionBefore(run.getMaxAddress().add(1));
        return returns && last != null && run.contains(last.getAddress());
    }

    // The text ends after the last function analysis found from the entry point; the data follows.
    private Address textEnd(MemoryBlock code) {
        Address last = code.getStart();
        for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
            if (code.contains(f.getEntryPoint())) {
                Address max = f.getBody().getMaxAddress();
                if (max.compareTo(last) > 0) last = max;
            }
        }
        return last.add(1);
    }
}
