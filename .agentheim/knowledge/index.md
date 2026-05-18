# Index

Top-level catalog of this project's bounded contexts, global decisions, and research.
For BC-scoped artifacts, see each BC's `INDEX.md`.

> Updated by: `model` (BC creation), `work` (global ADRs), `research` (reports tagged global / cross-BC), backfill script.
> Hand-edits are fine but the skills will append at the section markers below.

---

## Bounded contexts

<!-- bc-list:start -->
- **main** -- The single bounded context for Utterheim. Owns everything from voice acquisition through synthesis to delivery: voice profile management, sample capture, speak-request queueing, streaming TTS playback, the Claude Code integration surface, and the tray UI that wraps it all. -- `contexts/main/INDEX.md`
<!-- bc-list:end -->

## Global ADRs (scope: global)

<!-- adr-global:start -->
- **0001** -- Adopt .NET 9 / WPF / WPF-UI / Windows x64 stack -- 2026-05-01 -- `knowledge/decisions/0001-stack-net9-wpf-x64.md`
- **0002** -- Run pocket-tts as a managed Python sidecar over loopback HTTP -- 2026-05-01 -- `knowledge/decisions/0002-pocket-tts-python-sidecar.md`
- **0003** -- Expose utterheim's speak endpoint over loopback HTTP (JSON) -- 2026-05-01 -- `knowledge/decisions/0003-claude-transport-http.md`
- **0004** -- Stop signal drains the queue by default -- 2026-05-01 -- `knowledge/decisions/0004-stop-drains-queue.md`
- **0005** -- Voice profiles as folder-per-voice + library.json index -- 2026-05-01 -- `knowledge/decisions/0005-voice-persistence-layout.md`
- **0006** -- Reuse WhisperHeim infrastructure via copy-and-modify in v1 -- 2026-05-01 -- `knowledge/decisions/0006-whisperheim-reuse-copy-and-modify.md`
- **0007** -- Speak queue lives in the C# host as a Channel<T> -- 2026-05-01 -- `knowledge/decisions/0007-queue-channel-in-host.md`
- **0008** -- Cross-cutting — logging, errors, model bootstrap, distribution -- 2026-05-01 -- `knowledge/decisions/0008-cross-cutting-concerns.md`
- **0015** -- Utterheim-owned Python sidecar wrapper for /export-voice -- 2026-05-04 -- `knowledge/decisions/0015-utterheim-sidecar-wrapper.md`
<!-- adr-global:end -->

## Cross-BC research

Research reports relevant to more than one BC (or to the project as a whole). BC-specific
reports are listed in each BC's `INDEX.md`.

<!-- research-global:start -->
- **kyutai-tts** -- Kyutai Pocket TTS for a local Windows tray TTS service with sample-based voice cloning -- 2026-05-01 -- `knowledge/research/kyutai-tts-2026-05-01.md`
- **pocket-tts-german-support** -- Kyutai pocket-tts German language support — model variants, runtime selection, voice cloning, and plugin integration -- 2026-05-18 -- `knowledge/research/pocket-tts-german-support-2026-05-18.md`
<!-- research-global:end -->

## Pointers

- Vision: `vision.md`
- Context map: `context-map.md` (if exists)
- Protocol (chronological log): `knowledge/protocol.md` -- newest entries on top
- All ADRs: `knowledge/decisions/`
- All research: `knowledge/research/`
