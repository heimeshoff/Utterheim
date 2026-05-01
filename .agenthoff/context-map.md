# Context map: Mockingbird

## Decision: single bounded context

Mockingbird is a personal tool with a single user, a single primary consumer (Claude Code), and a tightly coupled language. After pressure-testing five candidate boundaries (synthesis, voice-library, voice-capture, claude-bridge, tray-ui), all five collapse into one coherent context.

The single BC lives at `contexts/main/`.

## Why one context, not five

The candidate boundaries failed the "different language, different rhythm, different actors, different invariants" test:

- **voice-library + voice-capture share language and lifecycle.** Capture's output *is* a library entry. The terms (`voice profile`, `sample clip`, `built-in voice`, `cloned voice`) span both. The actor is the same person doing the same thing: "make a voice usable." Capture is a workflow *into* the library, not a separate subject. Splitting them would weld two thin contexts together by every interesting interaction.

- **claude-bridge is a transport adapter, not a domain.** The vocabulary the "bridge" wants to own (`speak request`, `speak queue`, `stop signal`) belongs to whoever decides *what plays next* — and that's synthesis. HTTP vs named pipe vs CLI invocation is a delivery choice the architect makes; it doesn't carry domain semantics. Treat the Claude integration as a published interface (an open host) on the synthesis context, not its own BC.

- **synthesis owns the queue and the stop semantics.** The vision's open question — "does stop drain the queue or just halt the current utterance?" — is a synthesis-domain question, not a transport question. Putting the queue in a separate BC would force the bridge to know the queue's invariants anyway.

- **tray-ui is a presentation layer.** It surfaces voice-library management, voice-testing, and settings — all over the same domain model. UI is rarely a BC in DDD, and there's no domain language unique to "the window." It's a view, not a context.

- **WhisperHeim's audio plumbing is a library dependency, not a context.** `HighQualityLoopbackService`, `GlobalHotkeyService`, etc. are technical wrappers around WASAPI and Win32. They have no ubiquitous language and no invariants of their own. They get consumed (via shared library, copy-and-modify, or submodule — TBD during foundation) but they don't appear on the context map.

## Why this isn't under-splitting

A single BC works here because:

1. **One actor, one rhythm cluster.** The user does fast read paths (Claude triggers a speak request) and slow write paths (cloning a new voice every few weeks). Both rhythms live happily inside one BC; they don't pull in different directions strongly enough to justify a split.
2. **No language divergence.** A "voice profile" means exactly the same thing whether it's being created, listed, selected, or invoked. There's no point at which the same word means two different things.
3. **The invariants are shared.** "A speak request must reference a known voice profile" couples synthesis and library at the invariant level — exactly the signal that says *don't split these*.
4. **It's one developer's personal tool.** No team boundary is forcing artificial decomposition. The cost of context boundaries (translation, ACLs, integration tests) buys nothing here.

## Reconsideration triggers

Revisit this decision if any of the following happen:

- A second TTS engine is added with materially different vocabulary (multi-engine routing might warrant a synthesis-engine context separated from voice-management).
- The Claude integration grows beyond a thin transport — e.g., session-aware routing, conversation history, multi-LLM brokering. That would be its own subject with its own language.
- A second consumer joins (a non-Claude app, a remote machine). The published interface would need protocol semantics distinct from synthesis internals.
- Voice sharing or marketplace features appear (explicitly non-goal in v1, but would change the picture).

## The single BC at a glance

| Context | Path | Classification | Purpose |
|---|---|---|---|
| **main** | `contexts/main/` | mixed (see README) | Everything: voice management, capture, synthesis, queue/playback, Claude integration, tray UI |

See `contexts/main/README.md` for the full BC description, language, and actors.

## External dependencies (not BCs)

These are outside the context frame — they are consumed but not owned:

- **pocket-tts** (Kyutai) — the synthesis engine. Python package or sherpa-onnx ONNX port. Conformist relationship: mockingbird adapts to pocket-tts's API shape (TTSModel, voice state, .safetensors, Mimi encoder). Wrap it behind a thin engine interface so a future second engine can slot in.
- **WhisperHeim shared services** — audio capture (`HighQualityLoopbackService`, `LoopbackCaptureService`, `AudioCaptureService`), global hotkeys (`GlobalHotkeyService`), settings/data path (`DataPathService`, `SettingsService`), startup (`StartupService`). Reused as a library dependency. Form (shared lib vs copy-and-modify vs submodule) is a foundation-step decision.
- **Windows platform APIs** — WASAPI (via NAudio), Win32 hotkey hooks, tray icon, Mica/Fluent UI shell. Conformist to the platform.
