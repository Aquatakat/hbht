# 0016. Sequences keep the original's event stream

Status: Accepted · Oct 9, 2026

## Context

[0005](0005-plugin-format.md) says asset files use open formats: glTF, PNG, and WAV or OGG for audio. Every sound in the original is a Borg12 sequence: rows of events that play Borg11 samples, which its own sequencer steps ([audio](../formats/audio.md)). No open format holds that faithfully. MIDI and the tracker formats can't hold a pitch in Hz or the original's tempo and volume rules without rounding. Rendering every sequence to audio would lose looping, the sequence's own volume and pan, and its limits on voices, and the 476 of them would take gigabytes as WAV.

The events could go in the records as JSON, but there are 2.67 million of them. That's tens of megabytes the loader would read at every start, for data a modder is unlikely to edit by hand: a new piece of music is far more likely to arrive as a recording.

## Decision

- **A sequence's record holds what describes it:** its tracks, its loop position, and its instruments, each naming its sample (`aidyn:sample/…`) and its loop.
- **Its event stream is an asset, `sequence/<key>.dcm`, in the original's encoding,** byte for byte. The format doc is its specification. The runtime reads one only when it plays the sequence.
- **Samples are WAV,** as 0005 says: mono 16-bit, with 8-bit samples widened as the original's driver widens them.

## Consequences

- The port plays sequences with a port of the original's sequencer (`AudioEngine`), which is exact by construction and can be tested against the original's rules.
- A modder can replace a sequence's samples or loops by patching its record, and its music by replacing its `.dcm`. A tool to write `.dcm` files is mod-editor work.
- Recorded audio from mods (0005's OGG) will need a second kind of sound that the runtime streams rather than sequences. Whatever plays a sound should name it by record ID, so either kind can stand behind one.
