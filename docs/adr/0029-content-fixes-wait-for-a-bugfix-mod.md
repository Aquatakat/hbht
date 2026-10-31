# 0029. The base import fixes only content that breaks the game; the rest waits for a bugfix mod

Status: Accepted · Oct 25, 2026 · Adds to [0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) · Added to for imported assets by [0030](0030-the-import-welds-the-seams.md)

## Context

[0018](0018-the-port-fixes-bugs-and-sets-enhancements.md) says the port fixes the original's bugs outright, one at a time, as the user decides. It was written about the engine: what the code does. Some bugs are in the content instead, the data the importer writes into the base plugin.

The harbour reunion shows the difference ([scripts](../systems/scripts.md#faults)). Node 24 of script 1040, where Dougal's actor is made, has lost its link to node 25. With Dougal in the party, 1.0 runs off the end of the script and softlocks. The runner already ends any script that runs out of nodes, as `EndDialogue` would, so the port doesn't softlock: the scene just ends early. Linking node 24 to node 25 in the import would play the whole scene, but it would mean editing imported data by hand, one script at a time, in the plugin that's meant to be the ROM's content as the ROM has it.

The importer already makes one such fix, the Roog Cave's mushroom (`ImportOptions.FixMushroom`, [hardcoded content](../hardcoded-content.md)), which blocks the way through.

## Decision

- **The base import fixes content only where the bug breaks the game:** a softlock, or a way the player can't get past. Each is the user's choice, is an import option that's on by default, and is documented as a fix.
- **Where the engine already keeps the game going, that's enough.** A content bug the runner or the rules turn into something playable, such as a scene that ends early, stays as the ROM has it.
- **Other content fixes wait for a bugfix mod.** Once the mod tools exist (Phase 5), the content fixes the user chooses go into a mod of their own, as patches to the base plugin's records, not into the import. Until then, each is noted in its system's doc and listed in the roadmap's Phase 5.

## Consequences

- The base plugin stays the ROM's content, but for the few fixes the game needs to be finishable.
- Script 1040's node 24 isn't fixed: with Dougal in the party, the harbour reunion ends where Dougal's actor is made, and Keelin can't be recruited there. It's the bugfix mod's first entry.
- A content fix in the bugfix mod is a patch a player can leave out, like any mod.
