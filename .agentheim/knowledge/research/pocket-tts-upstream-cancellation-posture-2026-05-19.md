---
topic: kyutai-labs/pocket-tts upstream posture on cancellation / stop / interrupt support (H5 for main-045)
date: 2026-05-19
requested_by: main-045 (spike worker)
related_tasks: [main-045, main-046]
related_adrs: [0027]
---

# Research: pocket-tts upstream cancellation-PR feasibility (H5 for main-045)

## Question

main-045 hypothesis H5 — would a PR to `kyutai-labs/pocket-tts` adding a
first-class cancellation hook (e.g. a `stop_event: threading.Event | None`
parameter on `TTSModel.generate_audio_stream` checked inside
`_autoregressive_generation`'s `for generation_step in range(max_gen_len)`
loop) plausibly land in a useful timeframe? The verdict matters for ADR 0027:

- **If yes** (responsive upstream, accepting feature PRs): option (a) — carry
  an upstream patch as a local diff until the contribution lands — gains
  weight as a medium-term plan. The monkey-patch in option (e) becomes the
  bridge fix, retired after the next pocket-tts release.
- **If no** (quiet upstream, slow releases, no triage): option (e)'s
  monkey-patch becomes the durable solution rather than a bridge.

## Method limitation

This worker thread is sandboxed — no live HTTP/git access to github.com or
pypi.org. The findings below synthesise:

1. What the previous research note `kyutai-tts-2026-05-01.md` recorded
   (project name, license, release cadence as of 2026-05-01).
2. What the bundled pocket-tts 2.x source (read by main-045 refinement and
   captured in ADR 0026 / 0027) tells us about the project's API-stability
   posture.
3. Cross-references to the user's own constraints (we ship a managed
   embeddable Python; `pip install -U` is gated by our bootstrapper).

A follow-up pass with live network access should reconfirm sections 1–3
before main-046 commits to option (a). For the spike's purpose — picking
between option (a) (upstream-first) and option (e) (monkey-patch) as the
durable mechanism — the synthesis below is sufficient.

## Findings

### 1. Project velocity (as of last live data, 2026-05-01)

From `kyutai-tts-2026-05-01.md`:

- **PyPI 2.0.0 released 2026-04-21.** That's the canonical "official"
  package; we pin `pocket-tts>=2.0.0,<3` via PythonRuntimeBootstrapper.
  The 2.0.0 release came roughly 3 months after the 2026-01-13 launch
  blog ("Pocket TTS: a high-quality TTS with voice cloning that runs on
  CPU").
- **HuggingFace model hosting at `kyutai/pocket-tts`** plus the
  sibling `kyutai/pocket-tts-without-voice-cloning` — the project is a
  first-party Kyutai product, not a community fork.
- **Browser/WASM demo site at `kyutai-labs.github.io/pocket-tts/`** — the
  team maintains polish surfaces, suggesting active stewardship.
- **Community ports already exist** (PocketTTS.cpp, sherpa-onnx ONNX
  build, TorchSharp, Rust/Candle, WASM). Active downstream ecosystem
  is a positive signal for upstream attention; ports surface API
  questions back to maintainers.

Interpretation: Kyutai is a research lab that ships products and follows
through (Moshi, Mimi, Hibiki, Unmute, the 1.6B TTS, and now pocket-tts).
The 3-month cadence from launch to 2.0.0 is healthy but not aggressive —
not a daily-commit project but not abandonware either.

### 2. API stability posture (inferred from current source)

The bundled pocket-tts 2.x has been read in detail by main-045 refinement
and by ADR 0026. Two signals about how seriously the maintainers
treat their internal contracts:

- **Zero cancellation surface exists today.** Across
  `generate_audio`, `generate_audio_stream`, `_autoregressive_generation`,
  `_generate`, `_generate_audio_stream_short_text`, `_run_flow_lm_and_increment_step`,
  and the `pocket_tts.main` wrapper — no `cancel`, `stop`, `interrupt`,
  `stop_event`, `should_stop`, or `Event` parameter, no
  `is_disconnected`-style check. The omission appears to be deliberate
  (the codebase is research-oriented, optimised for "run the model end
  to end") rather than a oversight that should be cheap to fix from
  upstream's view.
- **The wrapper is a thin FastAPI app** (`pocket_tts.main`). It is the
  natural home for a cancellation hook, but its current shape suggests
  the upstream team treats it as a demo surface — production HTTP
  cancellation is downstream's problem to solve. The exact stance
  utterheim has adopted via `utterheim_sidecar` (ADR 0015) is
  effectively the "right" engagement model with this project: stay
  outside the package, wrap it.

Interpretation: a PR adding `stop_event: threading.Event | None = None`
to `generate_audio_stream` and threading it into
`_autoregressive_generation` would be **structurally clean** (no new
deps, ~10 lines), **semantically additive** (default `None` preserves
2.x behaviour), and **plausibly accepted** if maintainers are doing
triage. But the timeline is uncertain.

### 3. Acceptance timeline estimate (without live data)

Assuming the team takes external PRs seriously:

- **Optimistic:** PR reviewed and merged within 2–6 weeks; cancellation
  hook ships in pocket-tts 2.1.0 within 2–3 months. Our monkey-patch
  retires when the bootstrapper bumps the pin.
- **Realistic:** PR sits in the queue for 1–3 months while the team
  works on the next release. We carry the monkey-patch in production
  for at least one release cycle.
- **Pessimistic:** PR is acknowledged but the team prefers their own
  API design (e.g. they want an `async def` overload, not a `threading.Event`);
  the discussion drags through several rounds. We carry the monkey-patch
  for 6+ months.

None of these scenarios are bad for utterheim — option (e)'s monkey-patch
is cheap enough to carry through any of them. **But none of them justify
deferring the fix to "wait for upstream".** The user's leak is a release
blocker today; the monkey-patch must land regardless.

## Verdict for H5

**Hybrid posture is correct for ADR 0027 disposition:**

1. **Pick option (e)** — hybrid wrapper + monkey-patch — as the durable
   mechanism for v1. Land it via main-046. Do not block on upstream.
2. **Independently, file an issue / PR upstream** asking for a
   first-class cancellation hook. If accepted, the bootstrapper's
   `pocket-tts>=2.0.0,<3` pin can be tightened to `>=2.x.y` where
   `2.x.y` is the first release with the hook, and the monkey-patch
   becomes dead code (delete in a follow-up task).
3. **Do not pick option (a) (local file diff).** Even with a responsive
   upstream, the diff carries the same "pin breaks on refactor" risk
   as the monkey-patch but adds bootstrapper complexity (detect missing
   patch, re-apply on `pip install -U`). Option (e) dominates (a) on
   every axis except theoretical purity.

## What this note does NOT establish (open follow-ups)

- **Live commit cadence on `kyutai-labs/pocket-tts`** between 2026-04-21
  (2.0.0 release) and 2026-05-19 (today). A simple `git log --since`
  against the public repo would resolve this in seconds; not done here
  because of sandbox limits.
- **Whether any open issue/PR already discusses cancellation/stop/interrupt**
  on the kyutai-labs/pocket-tts tracker. If one exists and is closed
  "won't fix", that flips H5 to "no" and strengthens the option (e)
  recommendation further. If one is open and active, the option (a)
  case strengthens.
- **Maintainer responsiveness on similar feature PRs.** A pass through
  recent merged PRs (median time-to-merge, ratio of accepted to closed
  external contributions) would calibrate the timeline estimate.

These are all cheap to resolve later. None of them block main-046; they
inform whether to file an upstream issue alongside it.

## References

- `kyutai-tts-2026-05-01.md` — original project research, 2026-05-01.
- ADR 0026 — Stop cancels in-flight synthesis within ≤2 s (the contract).
- ADR 0027 — Cancellation propagation mechanism (the choice this note
  feeds into).
- main-045 — Spike: this note's parent.
- main-046 — Fix: lands option (e) regardless of upstream posture.
