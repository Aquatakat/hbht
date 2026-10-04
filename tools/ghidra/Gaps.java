// Lists the undisassembled runs inside the code's text range that hold non-zero words.
// @category Aidyn

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.*;

public class Gaps extends GhidraScript {
    @Override
    protected void run() throws Exception {
        Listing listing = currentProgram.getListing();
        Address end = toAddr(0x800ccec0L);
        Address a = toAddr(0x80000400L);
        int runs = 0, prologues = 0; long words = 0;
        StringBuilder sample = new StringBuilder();
        while (a.compareTo(end) < 0) {
            if (listing.getInstructionContaining(a) == null && getInt(a) != 0) {
                Address s = a; int n = 0;
                while (a.compareTo(end) < 0 && listing.getInstructionContaining(a) == null) { if (getInt(a) != 0) n++; a = a.add(4); }
                runs++; words += n;
                // first non-zero word of the run
                Address f = s; while (getInt(f) == 0) f = f.add(4);
                boolean pro = (getInt(f) >>> 16) == 0x27BD;
                if (pro) prologues++;
                if (runs <= 40) sample.append(String.format("%s %d words%s; ", f, n, pro ? " prologue" : String.format(" %08x", getInt(f))));
            } else a = a.add(4);
        }
        println(String.format("gap runs %d, non-zero words %d, starting with addiu sp %d", runs, words, prologues));
        println(sample.toString());
    }
}
