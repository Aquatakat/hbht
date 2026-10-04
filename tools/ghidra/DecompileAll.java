// Writes every function's pseudocode to out/ghidra/<rom>.c, each headed "//@ 0x<address> <name>",
// which tools/n64dis.py --c reads. Ghidra's output from the original's code: it stays in out/.
// @category Aidyn

import java.io.*;
import java.nio.charset.StandardCharsets;

import ghidra.app.decompiler.*;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;
import ghidra.program.model.mem.MemoryBlock;

public class DecompileAll extends GhidraScript {
    @Override
    protected void run() throws Exception {
        MemoryBlock code = currentProgram.getMemory().getBlock("code");
        DecompInterface decompiler = new DecompInterface();
        decompiler.toggleCCode(true);
        decompiler.openProgram(currentProgram);
        String rom = currentProgram.getName().replace(".bin", "");
        File file = new File(new File(currentProgram.getExecutablePath()).getParentFile(), rom + ".c");
        int done = 0, failed = 0;
        try (PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(file), StandardCharsets.UTF_8))) {
            for (Function f : currentProgram.getFunctionManager().getFunctions(true)) {
                if (!code.contains(f.getEntryPoint())) continue;
                w.printf("//@ 0x%08x %s%n", f.getEntryPoint().getOffset(), f.getName());
                DecompileResults r = decompiler.decompileFunction(f, 60, monitor);
                if (r.decompileCompleted()) { w.println(r.getDecompiledFunction().getC().strip()); done++; }
                else { w.println("// decompile failed: " + r.getErrorMessage()); failed++; }
                w.println();
            }
        }
        decompiler.dispose();
        println(String.format("wrote %s: %d functions, %d failed", file, done, failed));
    }
}
