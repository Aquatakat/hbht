// Names the program's functions and globals from the project's symbol map, tools/symbols/<rom>.txt
// (lines of "name = 0xaddress;", roadmap 0.8), so the pseudocode reads with our names. Re-running it
// replaces the names it set before.
// @category Aidyn

import java.io.File;
import java.nio.file.Files;
import java.util.regex.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.*;

public class ApplySymbols extends GhidraScript {
    private static final Pattern LINE = Pattern.compile("\\s*([\\w:~]+)\\s*=\\s*0x([0-9a-fA-F]+);.*");

    @Override
    protected void run() throws Exception {
        String rom = currentProgram.getName().replace(".bin", "");
        File map = new File(getSourceFile().getParentFile().getParentFile().getAbsolutePath(), "symbols/" + rom + ".txt");
        if (!map.exists()) { println("no symbol map at " + map); return; }
        SymbolTable table = currentProgram.getSymbolTable();
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
                Symbol s = table.getPrimarySymbol(a);
                if (s != null && s.getSource() == SourceType.USER_DEFINED) s.setName(name, SourceType.USER_DEFINED);
                else table.createLabel(a, name, SourceType.USER_DEFINED).setPrimary();
                labels++;
            }
        }
        println(String.format("named %d functions and %d other addresses from %s", functions, labels, map.getName()));
    }
}
