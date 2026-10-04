// Writes the code index (roadmap 0.7) that tools/n64dis.py reads: out/ghidra/<rom>.index.json.
// Per function: its range, its calls, the addresses it reads, writes or takes (lui pairs included),
// the strings among them, and its jump tables. For the segment: the strings in its data, and every
// word in the data that points into the segment or BSS (vtables, callback tables, string tables).
// Usage: tools/ghidra.ps1 run us ExportIndex.java
// @category Aidyn

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.mem.*;
import ghidra.program.model.symbol.*;

public class ExportIndex extends GhidraScript {
    private MemoryBlock code;
    private Address textEnd;
    private final TreeMap<Long, String> strings = new TreeMap<>();

    @Override
    protected void run() throws Exception {
        code = currentProgram.getMemory().getBlock("code");
        FunctionManager functions = currentProgram.getFunctionManager();
        textEnd = code.getStart();
        for (Function f : functions.getFunctions(true))
            if (code.contains(f.getEntryPoint()) && f.getBody().getMaxAddress().compareTo(textEnd) >= 0) textEnd = f.getBody().getMaxAddress().add(1);
        findStrings();

        String rom = currentProgram.getName().replace(".bin", "");
        File file = new File(new File(currentProgram.getExecutablePath()).getParentFile(), rom + ".index.json");
        try (PrintWriter w = new PrintWriter(new OutputStreamWriter(new FileOutputStream(file), StandardCharsets.UTF_8))) {
            w.printf("{\"rom\":\"%s\",\"text\":[%d,%d],\"segmentEnd\":%d,\"functions\":[%n", rom, code.getStart().getOffset(), textEnd.getOffset(), code.getEnd().getOffset() + 1);
            boolean first = true;
            for (Function f : functions.getFunctions(true)) {
                if (!code.contains(f.getEntryPoint())) continue;
                if (!first) w.println(",");
                first = false;
                writeFunction(w, f);
            }
            w.println("],\"strings\":{");
            first = true;
            for (Map.Entry<Long, String> s : strings.entrySet()) {
                if (!first) w.println(",");
                first = false;
                w.printf("\"%d\":%s", s.getKey(), json(s.getValue()));
            }
            w.println("},\"pointers\":[");
            first = true;
            // Words in the data that point into the segment or BSS: [where, to].
            for (Address a = textEnd; a.compareTo(code.getEnd()) < 0; a = a.add(4)) {
                long v = getInt(a) & 0xffffffffL;
                if (v >= 0x80000400L && v < bssEnd() && v != 0x80000400L) {
                    if (!first) w.print(",");
                    first = false;
                    w.printf("[%d,%d]", a.getOffset(), v);
                }
            }
            w.println("]}");
        }
        println("wrote " + file + ": " + functions.getFunctionCount() + " functions, " + strings.size() + " strings");
    }

    private long bssEnd() {
        MemoryBlock bss = currentProgram.getMemory().getBlock("bss");
        return bss.getEnd().getOffset() + 1;
    }

    private void writeFunction(PrintWriter w, Function f) {
        TreeSet<Long> calls = new TreeSet<>();
        TreeMap<Long, String> refs = new TreeMap<>();
        TreeMap<Long, TreeSet<Long>> jumpTables = new TreeMap<>();
        ReferenceManager rm = currentProgram.getReferenceManager();
        for (Instruction i : currentProgram.getListing().getInstructions(f.getBody(), true)) {
            for (Reference r : rm.getReferencesFrom(i.getAddress())) {
                long to = r.getToAddress().getOffset();
                RefType t = r.getReferenceType();
                if (!r.getToAddress().isMemoryAddress()) continue;
                if (t.isCall()) { calls.add(to); continue; }
                if (t.isComputed() && t.isJump()) { jumpTables.computeIfAbsent(i.getAddress().getOffset(), k -> new TreeSet<>()).add(to); continue; }
                if (t.isFlow()) continue;
                if (f.getBody().contains(r.getToAddress())) continue;
                String kind = t.isWrite() ? "w" : t.isRead() ? "r" : "a";
                refs.merge(to, kind, (a, b) -> a.contains(b) ? a : a + b);
            }
        }
        luiRefs(f, refs);
        w.printf("{\"at\":%d,\"end\":%d", f.getEntryPoint().getOffset(), f.getBody().getMaxAddress().getOffset() + 1);
        if (f.getSymbol().getSource() != SourceType.DEFAULT) w.printf(",\"name\":%s", json(f.getName()));
        if (isUnreferenced(f)) w.print(",\"unreferenced\":true");
        w.print(",\"calls\":" + list(calls));
        w.print(",\"refs\":[");
        boolean first = true;
        for (Map.Entry<Long, String> r : refs.entrySet()) {
            if (!first) w.print(",");
            first = false;
            w.printf("[%d,\"%s\"]", r.getKey(), r.getValue());
        }
        w.print("]");
        if (!jumpTables.isEmpty()) {
            w.print(",\"jumps\":[");
            first = true;
            for (Map.Entry<Long, TreeSet<Long>> j : jumpTables.entrySet()) {
                if (!first) w.print(",");
                first = false;
                w.printf("[%d,%s]", j.getKey(), list(j.getValue()));
            }
            w.print("]");
        }
        w.print("}");
    }

    private static final Set<String> LOADS = Set.of("lb", "lbu", "lh", "lhu", "lw", "lwu", "ld", "lwc1", "ldc1", "lwl", "lwr");
    private static final Set<String> STORES = Set.of("sb", "sh", "sw", "sd", "swc1", "sdc1", "swl", "swr");

    // Ghidra's references miss many lui pairs, a jump table's address among them. Follow each lui
    // through the function in address order to the addiu, ori, load or store that completes it,
    // forgetting a register when something else writes it.
    private void luiRefs(Function f, TreeMap<Long, String> refs) {
        Map<Object, Long> upper = new HashMap<>();
        for (Instruction i : currentProgram.getListing().getInstructions(f.getBody(), true)) {
            String m = i.getMnemonicString().replace("_", "");
            if (m.equals("lui")) {
                upper.put(i.getOpObjects(0)[0], (long) (int) (i.getScalar(1).getUnsignedValue() << 16));
                continue;
            }
            Long hi = null;
            Long lo = null;
            for (int n = 0; n < i.getNumOperands(); n++) {
                for (Object o : i.getOpObjects(n)) {
                    if (n > 0 && upper.containsKey(o)) hi = upper.get(o);
                    if (o instanceof ghidra.program.model.scalar.Scalar s) lo = s.getSignedValue();
                }
            }
            if (hi != null && lo != null && (LOADS.contains(m) || STORES.contains(m) || m.equals("addiu") || m.equals("ori"))) {
                long address = (m.equals("ori") ? (hi | (lo & 0xffff)) : hi + (short) (long) lo) & 0xffffffffL;
                if (address >= 0x80000000L && address < 0x80800000L && !f.getBody().contains(toAddr(address))) {
                    String kind = STORES.contains(m) ? "w" : LOADS.contains(m) ? "r" : "a";
                    refs.merge(address, kind, (a, b) -> a.contains(b) ? a : a + b);
                }
            }
            for (Object o : i.getResultObjects()) upper.remove(o);
        }
    }

    private boolean isUnreferenced(Function f) {
        for (Bookmark b : currentProgram.getBookmarkManager().getBookmarks(f.getEntryPoint()))
            if (b.getCategory().equals("Aidyn") && b.getComment().startsWith("Unreferenced")) return true;
        return false;
    }

    // NUL-terminated runs of at least three printable bytes in the data after the text, word-aligned
    // as the compiler places them.
    private void findStrings() throws Exception {
        for (Address a = textEnd; a.compareTo(code.getEnd()) < 0; a = a.add(4)) {
            StringBuilder s = new StringBuilder();
            Address b = a;
            while (b.compareTo(code.getEnd()) < 0) {
                int c = getByte(b) & 0xff;
                if (c == 0) break;
                if (c < 0x20 && c != '\n' && c != '\t' && c != '\r' || c >= 0x7f) { s = null; break; }
                s.append((char) c);
                b = b.add(1);
            }
            if (s != null && s.length() >= 3 && b.compareTo(code.getEnd()) < 0) {
                strings.put(a.getOffset(), s.toString());
                long next = (b.getOffset() + 4) & ~3L;
                a = toAddr(next - 4);
            }
        }
    }

    private static String list(Collection<Long> values) {
        StringBuilder b = new StringBuilder("[");
        for (Long v : values) { if (b.length() > 1) b.append(","); b.append(v); }
        return b.append("]").toString();
    }

    private static String json(String s) {
        StringBuilder b = new StringBuilder("\"");
        for (char c : s.toCharArray()) {
            if (c == '"' || c == '\\') b.append('\\').append(c);
            else if (c < 0x20) b.append(String.format("\\u%04x", (int) c));
            else b.append(c);
        }
        return b.append('"').toString();
    }
}
