---
id: 0024
title: Sidecar preloads a fixed list of languages at startup (English + German for v1)
scope: main
status: accepted
date: 2026-05-18
supersedes: []
superseded_by: []
related_tasks: [main-036, main-039]
related_research: [pocket-tts-german-support-2026-05-18]
---

# ADR 0024: Sidecar preloads a fixed list of languages at startup

## Context

pocket-tts binds language at **model-instance load time** via
`TTSModel.load_model(language=...)`. The generation methods
(`generate_audio`, `generate_audio_stream`) take no language argument, so a
single resident `TTSModel` can only ever produce one language for the lifetime
of the process (see research `pocket-tts-german-support-2026-05-18` section 3,
and the per-instance binding finding restated in ADR 0023).

Today the utterheim sidecar's `serve` command loads exactly **one** model and
assigns it to `pocket_tts.main.tts_model` (see main-011's outcome and the
2.0.0+ behaviour locked in by main-043). That is sufficient for an
English-only product, but Utterheim has now committed to serving English and
German concurrently from the tray app.

ADR 0023 made the partner decision: each voice profile carries its language,
and the sidecar routes each `/speak` request to the resident `TTSModel`
matching the named voice's declared language. That decision is only coherent
if the sidecar actually *has* a resident model for every language any voice
in the library might name. The question this ADR closes is: **how does the
sidecar get into that state, and when?**

Structural context this builds on:

- **ADR 0002** — pocket-tts runs as a single managed Python sidecar over
  loopback HTTP. There is one Python process, and whatever model-loading
  strategy we pick happens inside it.
- **ADR 0007** — the speak queue lives in the C# host as a `Channel<T>`. The
  host has already chosen *which request to send next* by the time the
  sidecar sees it; routing inside the sidecar is purely a language-to-model
  lookup, not a scheduling decision.
- **ADR 0023** — voice profile carries its language; speak HTTP body
  unchanged. The sidecar's `voice_id → language` map is the routing key. This
  ADR is the partner decision that guarantees every language that map can
  produce has a model resident to point at.

The user has explicitly de-prioritised the Windows RAM-validation spike (see
the research's "Resident-RAM measurement on Windows" open question) — their
hardware has enough headroom that the empirical per-model cost (~135 MB on
Linux/macOS, single-source community measurement; ~270 MB for en+de) is not
a blocking concern for v1.

## Decision

**The sidecar preloads a fixed list of languages at process startup.** For
v1 the list is `["english", "german"]`. The list is sidecar configuration,
not a per-request input.

Concretely:

- The sidecar `serve` command accepts a list of languages instead of a single
  language (the wire-level / CLI shape is an implementation detail for the
  implementing task main-039; the *decision* here is that the count moves
  from one to many).
- At startup the sidecar instantiates one `TTSModel` per configured language
  via `TTSModel.load_model(language=<name>)`. All instances stay resident
  for the process lifetime.
- The sidecar maintains a `language → TTSModel` map. Each `/speak` request
  is routed to the entry for the language declared by the named voice's
  profile (per ADR 0023).
- If a voice's declared language is **not** in the preloaded set, the
  sidecar returns an error rather than loading a model on demand. This keeps
  the "always-warm" guarantee honest and surfaces misconfiguration loudly
  instead of producing a slow-first-request surprise.
- Adding a third language is a **configuration change** (extend the list,
  restart the sidecar), not a code change.

## Consequences

### Positive
- **Every request stays warm.** No per-request model load, no per-request
  reload, no first-request latency cliff after a language switch. The warm
  sidecar pattern (ADR 0002) is preserved for every language we serve.
- **Routing is a dict lookup.** Combined with ADR 0023's voice-language map,
  a `/speak` request resolves to a `TTSModel` in O(1) without I/O.
- **Misconfiguration surfaces at startup, not at first use.** If the
  configured language list and the voice library disagree, the sidecar can
  detect it on boot (or on the first request that exposes it) and fail
  cleanly. Reload-on-change would have failed mid-request instead.
- **Cheap to extend within the small-N regime.** Going from 2 to 3 or 4
  languages is one line of config and one extra ~135 MB allocation. The user
  has explicitly stated RAM headroom is not a constraint for v1.
- **Trivially reversible.** If RAM ever becomes a constraint on a leaner
  deployment target, dropping back to single-language-per-process or
  switching to reload-on-change doesn't break ADR 0023's voice-carries-language
  contract — it only changes which voices can be served from one process.

### Negative
- **Startup time grows linearly with the language list.** Each extra language
  adds one `TTSModel.load_model` call to the boot path. For en+de this is
  acceptable; if the list ever grows to all six pocket-tts languages, boot
  time may need a usability review (parallel loads? lazy load with a "ready"
  signal per language?). Flagged as an open follow-up rather than designed
  for now.
- **Resident memory is paid up-front.** ~270 MB for en+de on the
  single-source community measurement, possibly more on Windows. The user
  has accepted this trade-off; revisit if a future deployment target is
  RAM-constrained.
- **A voice cloned for a language not on the preload list is unusable until
  the list is extended and the sidecar restarted.** The Voices page (main-041)
  language picker enumerates the same fixed set, so this is a configuration
  coupling, not a UX surprise — but it does mean "add a language" is not a
  hot-swap operation in v1.

### Neutral
- The sidecar's existing single-model code paths (`pocket_tts.main.tts_model`
  module-level slot) become a degenerate case of the multi-model map. The
  refactor is mechanical.
- The C# host (ADR 0007's queue owner) needs no change for this decision —
  it still dispatches one `/speak` at a time and doesn't care how many models
  are resident on the other end.

## Alternatives considered

The research's open-questions section enumerated three viable strategies for
making per-instance-bound pocket-tts serve more than one language. All three
were considered.

1. **Preload list — selected.** See Decision above. Picked because it keeps
   every request warm, makes routing a dict lookup, and the RAM cost is one
   the user has explicitly accepted.

2. **Reload-on-change.** Keep a single resident `TTSModel`; when a request
   arrives for a language different from the current one, swap models in
   place. **Rejected** because (a) model load takes seconds, which would
   present as a first-request latency cliff every time language alternates —
   exactly the pattern likely in a Claude-Code workflow that mixes English
   prompts and German responses; (b) it forces the sidecar to serialize
   *across languages* in a way the C# queue (ADR 0007) doesn't currently
   model, opening a head-of-line-blocking surprise for callers; (c) the
   "warm sidecar" framing of ADR 0002 is the whole reason the sidecar exists
   — reloading on demand fights the architecture.

3. **Single-default constraint.** Pick one language at sidecar boot and
   refuse requests for any other. **Rejected** because it makes the
   voice-carries-language decision (ADR 0023) operationally useless: voices
   could declare any language but only one would ever play. The user's
   stated goal is to serve English and German concurrently, so a
   single-default policy fails the requirement directly.

## Open follow-up

- **Sixth-language threshold.** This decision is correct in the small-N
  regime (1–4 languages). If a request comes in to support all six
  pocket-tts languages — or to add a `german_24l` variant alongside
  `german` — revisit whether preload-all-at-once still makes sense or
  whether a lazy-load-with-warmup or reload-on-change strategy starts to
  earn its weight. Triggering events: a sixth resident model is proposed,
  or the boot-time cost crosses a usability threshold on the user's
  hardware.

## References

- ADR 0002 — pocket-tts as a managed Python sidecar (the warm-sidecar
  framing this ADR preserves).
- ADR 0007 — speak queue lives in the C# host as a `Channel<T>` (the
  scheduling layer above this ADR's routing layer).
- ADR 0023 — voice profile carries its language; speak HTTP body unchanged
  (the partner decision — together they make the routing story coherent).
- Research: `pocket-tts-german-support-2026-05-18` — section 3
  (per-instance binding, ~135 MB per resident model, three implementation
  options); open questions section (the three strategies considered above,
  and the Windows RAM-measurement spike the user de-prioritised).
- main-011 — real pocket-tts engine landed; sidecar's `serve` currently
  loads one `TTSModel` and assigns it to `pocket_tts.main.tts_model`. This
  ADR's implementation (main-039) generalises that slot to a per-language
  map.
- main-039 — implements this decision in the sidecar.
