# Audio: Borg11 samples and Borg12 sequences (roadmap 1D)

The original has its own music and sound-effect format, not the N64 SDK's. It uses libultra's synthesizer to mix voices, but not its sequence player or sound banks (`alSeqp*` and `alBnk*` aren't linked), and none of the samples are ADPCM. Every sound in the game, from the menu's cursor tick to the battle music, is a **Borg12**: rows of events over up to 16 tracks, close to a tracker module, which play **Borg11** samples of raw PCM. The debug build names the sequencer `./src/dcm.cpp` and the Borg12 loader `./src/n64BorgDCMModule.cpp`, so the original calls a Borg12 a DCM module. The sequencer, voice manager and mixer, and how the port plays all this, are in [audio](../systems/audio.md#the-driver).

`hbht audio <rom>` writes every sample as WAV and renders every sequence to WAV with the port's player, with a `sequences.csv`; `--plays <n>` loops a sequence `n` times.

## Loops

Music loops back to its start, never to a point within it. A Borg12 has a loop position, but it's 0 in all 476, and there's no jump or pattern-order event. Whether a sequence loops is up to whatever plays it: the music players ask for a loop, and sound effects don't ([the driver](../systems/audio.md#the-driver) says how it loops).

What does loop mid-sample is an **instrument**: about half of them loop a stretch of their sample for as long as the note plays, and the rest play once.

## Borg11: a sample

A 0x10-byte header of four big-endian u32s, then the PCM, mono.

| Offset | Field |
| --- | --- |
| 0x00 | Flags: 0x18 for 16-bit (76 samples), 0x14 for 8-bit (73). 0x10 is always set. The driver tests only 0x04. |
| 0x04 | The PCM's length in bytes. |
| 0x08 | Its length in samples. A voice that plays once ends when it reaches this. |
| 0x0C | The PCM's offset, always 0x10. The fixup (`Borg11Sample.Parse`, `0x8009D580`) makes it a pointer. |

- **Both widths are signed.** 16-bit samples are big-endian. `dmaProc` (`0x8009001C`) widens 8-bit ones to 16 bits with `b << 8`, through a modified copy of libultra's 16-bit loader (`alRaw8Pull`, `0x800C4A34`), so the port keeps every sample as 16-bit.
- **There's no rate, base note, tuning or loop.** A sequence's events give the rate to play a sample at, in Hz, and its instruments give the loops. A few samples sit well off zero, kick drums among them.
- 440 to 75,947 samples long. No Borg12 plays 0x05BC, 0x0624, 0x0656 or 0x38E0.

## Borg12: a sequence

The file starts with a u32 offset of its sample list and a zero u32. The game hands the sequencer the file from offset 8, so the sequencer's header is the next six u32s, all big-endian:

| Offset | Field |
| --- | --- |
| 0x00 | The sample list's offset: a u32 Borg11 index per instrument, after the stream and 8-aligned. The fixup (`Borg12Sequence.Parse`, `0x8009D230`) loads each and points its instrument at it. |
| 0x04 | Zero. |
| 0x08 | Tracks, 1 to 16 in the data (the code reads a byte, and allows 32). |
| 0x0C | Instruments, 1 to 13. |
| 0x10 | The event stream's length in bytes. |
| 0x14 | The loop position in the stream. 0 in every Borg12. |
| 0x18 | The instrument table's offset, always 0x20. |
| 0x1C | The event stream's offset, always 0x20 + 16 × instruments. |

**An instrument** is 16 bytes: a sample pointer (0 in the ROM), the loop's start and end in samples, then a loop count in the last byte. 0xFF loops for ever (the driver reads it as −1) and 0 plays once. No other count occurs. 22 instruments that play once still carry a start and end, which nothing reads. The loop's end is the sample after the loop's last. Instrument 0 of 0x0566 (1,382) loops to 45,288 in a sample of 28,648, so the original plays past the sample's end ([the driver](../systems/audio.md#the-driver) says what the port plays there).

### The event stream

The stream is rows, read one at a time. A row has an event for each track, in track order, all in one stream: there's no per-track pointer. A track that's waiting reads nothing that row.

An event is a flag byte and the operands its bits ask for, in this order:

| Bit | Operand | What it does |
| --- | --- | --- |
| 0x80 | none | A wait instead of an event: the track reads nothing for the next `(b & 0x7F) + 1` rows. Never used. |
| 0x40 | u16, **little-endian** | The track's pitch (below). Without a note, it changes the playing note's pitch. |
| 0x20 | u8 `v` | The track's volume, `floor(sqrt(v))`, so 0 to 15. Without a note, it changes the playing note's. |
| 0x10 | u8 | The track's pan, 0 left to 255 right. Without a note, it changes the playing note's. |
| 0x08 | u8 | The track's instrument. |
| 0x04 | u8 | Where in the sample this event's note starts, `byte << 8` samples. Never used. |
| 0x01 | u8 `e`, then maybe one more | Effects: 0x80 cuts the track's note, 0x40 takes a tempo byte, 0x20 skips a byte. Only tempo occurs, 204 times. |
| 0x02 | none | Starts a note, after the rest: cuts the track's note and plays its instrument with its pitch, volume and pan. |

An event of 0 does nothing, and most events are. A track's first event always sets its pitch, volume, pan and instrument (0x78, often with 0x02 or 0x01 too), which matters because the original never clears a track's state between the sequences a player slot plays.

**Pitch** is the rate in Hz to play the sample at. The u16's top two bits choose a base and a shift: `(base[w >> 14] + ((w & 0x3FFF) << (w >> 14))) & 0xFFFF`, with bases 0, 0x4000, 0xC000 and 0x1C000 (`0x800D5284`), a float-like encoding that's finer at low pitches. The track keeps only 16 bits, so the pitches that would be above 65,535 Hz (8,331 events in 35 sequences) wrap round to low ones; [user-checks](../user-checks.md) asks whether that's audible. The voice plays at `pitch / 44,095` samples per output sample, at most 1.99996.

**Tempo** sets how many sequencer milliseconds pass between rows. A sequence starts with a row every 20 (tracker tempo 125). A tempo byte `x` makes it every `2500 / x`, in integers (`125000 / (x × 25 × 2) − 1` calls between rows). The data's tempos run 89 to 196, mostly 118 to 134. The longest sequence is 0x0632, the Expansion Pak's title music, at 4 minutes 6 seconds.

## In the base plugin

- **A sequence** is `aidyn:sequence/0xHHHH` in `records/sequences.json`: its tracks, its loop position and its instruments, each naming its sample with its loop. Its event stream is the asset `sequence/0xHHHH.dcm`, as the ROM has it ([ADR 0016](../adr/0016-sequences-keep-the-originals-events.md)).
- **What plays them** is on other records: the sounds engine code names in `aidyn:sounds/0`, the music on the map sets and their chunks, the audio keys on the entities, and weapons' and spells' sounds on theirs ([audio](../systems/audio.md#whats-ported)).
- **A sample** is the asset `sample/0xHHHH.wav`, mono 16-bit. It has no record. A WAV needs a rate, so each says the pitch its sample is most often played at, which is only for listening to it elsewhere: the player ignores it.
