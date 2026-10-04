// Prints Ghidra's pseudocode for the functions at the given addresses (or names).
// Usage: tools/ghidra.ps1 run us Decompile.java 0x80078d14 [0x... ...]
// @category Aidyn

import ghidra.app.decompiler.*;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public class Decompile extends GhidraScript {
    @Override
    protected void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true);
        decompiler.openProgram(currentProgram);
        for (String arg : getScriptArgs()) {
            Function f = arg.startsWith("0x") ? getFunctionContaining(toAddr(Long.parseUnsignedLong(arg.substring(2), 16)))
                : getGlobalFunctions(arg).stream().findFirst().orElse(null);
            if (f == null) { println("No function at " + arg); continue; }
            DecompileResults r = decompiler.decompileFunction(f, 60, monitor);
            println(r.decompileCompleted() ? "\n" + r.getDecompiledFunction().getC() : "Failed: " + r.getErrorMessage());
        }
        decompiler.dispose();
    }
}
