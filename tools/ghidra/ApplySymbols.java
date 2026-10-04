// Names the program's functions and globals from the project's symbol map (roadmap 0.8), so the
// pseudocode reads with our names. It reads out/ghidra/<rom>.symbols.txt, which tools/builds.py
// writes from tools/symbols/<rom>.txt with the other build's names carried across, or the map itself
// before that exists. Names it set before that have left the map are removed.
// @category Aidyn

import java.io.File;
import java.nio.file.Files;
import java.util.regex.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.*;

public class ApplySymbols extends GhidraScript {
    private static final Pattern LINE = Pattern.compile("\\s*([\\w:.~]+)\\s*=\\s*0x([0-9a-fA-F]+);.*");

    @Override
    protected void run() throws Exception {
        String rom = currentProgram.getName().replace(".bin", "");
        File map = new File(new File(currentProgram.getExecutablePath()).getParentFile(), rom + ".symbols.txt");
        if (!map.exists()) map = new File(getSourceFile().getParentFile().getParentFile().getAbsolutePath(), "symbols/" + rom + ".txt");
        if (!map.exists()) { println("no symbol map at " + map); return; }
        SymbolTable table = currentProgram.getSymbolTable();
        java.util.List<Symbol> ours = new java.util.ArrayList<>();
        for (Symbol s : table.getAllSymbols(true)) if (s.getSource() == SourceType.USER_DEFINED) ours.add(s);
        for (Symbol s : ours) {
            Function f = getFunctionAt(s.getAddress());
            if (f != null && f.getSymbol().equals(s)) f.setName(null, SourceType.DEFAULT);
            else s.delete();
        }
        int functions = 0, labels = 0;
        for (String line : Files.readAllLines(map.toPath())) {
            Matcher m = LINE.matcher(line);
            if (!m.matches()) continue;
            String name = m.group(1).replace("::", "__").replace("~", "_dtor_");
            Address a = toAddr(Long.parseUnsignedLong(m.group(2), 16));
            Function f = getFunctionAt(a);
            if (f != null) {
                f.setName(name, SourceType.USER_DEFINED);
                functions++;
            } else {
                table.createLabel(a, name, SourceType.USER_DEFINED).setPrimary();
                labels++;
            }
        }
        println(String.format("named %d functions and %d other addresses from %s", functions, labels, map.getName()));
    }
}
