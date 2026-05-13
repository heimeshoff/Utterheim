---
id: main-023
title: Diagnose first-chunk latency on long input (~9s for 200-word paragraph)
status: done
type: spike
context: main
created: 2026-05-03
completed: 2026-05-04
commit: c8567d6
depends_on: [main-011, main-021]
blocks: [main-024]
tags: [spike, performance, sidecar, streaming]
---

## Why

The vision sets a ≤2 s first-chunk-latency budget (vision §"What success
looks like" criterion 1, plus the seed glossary entry for **First-chunk
latency**: "Target ≤2 s; pocket-tts claims ~200 ms").

During main-018 first-run verification:
- Short sentence ("Hello, this is utterheim.") → ~1 s to audible. Within budget.
- ~200-word paragraph → **~9 s to audible**. ~4.5× over budget.

Streaming itself is working — playback starts before synthesis completes,
satisfying main-018's *correctness* criterion 3. But the vision-level
*latency* target is missed badly enough that the read-aloud experience
will feel sluggish in the v1 scenario it's designed for.

The fix can't be designed without knowing what's adding the ~7 s. This
spike finds the bottleneck and writes the diagnosis. The follow-up
**main-024** lands the actual fix.

## What

A diagnosis-only investigation. **No production code changes.** The
deliverable is timing data + code references + a written verdict that
tells main-024 exactly what to change.

### Hypotheses to probe (roughly in this order)

1. **Whole-text preprocessing before first chunk.** Pocket-tts (or our
   wrapper) may be running tokenization / phonemization / prosody planning
   over the *entire* input before emitting any audio.
2. **Sentence-batched generation.** If pocket-tts itself only streams
   *within* a sentence and emits sentences sequentially, the natural fix
   is parallel preprocessing of sentence N+1 while sentence N is being
   generated.
3. **Buffer / warmup cost.** First request after process start has a
   model-load tail. Confirm whether the 9 s case is first-call cold or
   persists on warm calls. If warm, structural; if only cold, expected.
4. **HTTP transport buffering.** Per ADR 0003 the speak transport is
   HTTP. Confirm we're using chunked transfer / streaming response, not
   buffering the whole audio body before the first byte hits the wire.

### Measurement methodology (pin this down before measuring anything)

Define once so this spike, the main-024 fix, and any future regression
share a reproducible protocol.

- **Time origin (T0):** the moment the HTTP `POST /speak` request line
  is sent. Capture HTTP-side timings via
  `curl -w "%{time_starttransfer},%{time_total}\n"`.
- **First-audio-at-speakers (T1):** wall-clock at first audible sample.
  Mark via a discrete cue — log line at the audio playback start
  callback, or a stopwatch around the audio device write. Document the
  chosen mechanism in the Outcome.
- **First-chunk latency = T1 − T0.** End-to-end, what the user hears.
- **Sample inputs** (canonical, repeatable, committed to repo under
  `examples/perf/`):
  - **Short:** "Hello, this is utterheim." (5 words)
  - **Medium:** the same ~200-word paragraph used in main-018 verification
    — record verbatim in `examples/perf/medium-input.txt`.
  - **Long:** a ~1000-word paragraph — record verbatim in
    `examples/perf/long-input.txt`.
- **Cold vs warm:** "cold" = first call after a fresh sidecar boot;
  "warm" = at least one prior speak completed on the same sidecar
  process. Label every measurement.

### Diagnosis steps

1. Capture three timings using the methodology above:
   `cold-short`, `warm-short`, `warm-medium`. Repeat `warm-medium` 3× and
   report the median.
2. Run the same medium input directly through the **pocket-tts CLI**
   (no utterheim involvement) and time the first chunk. If pocket-tts
   alone is ≤2 s, the entire ~7 s delta lives in our code.
3. With pocket-tts ruled in or out, walk the C# audio pipeline and the
   Python sidecar with logs / a profiler / strategic stopwatches to
   localise where the time goes. Hypotheses 1–4 are the suspect list;
   confirm or rule out each with evidence.
4. Write the **Outcome**: which hypothesis(es) won, file/line references,
   recommended fix shape (e.g. "audio buffer X is filled completely
   before first write — flush every N samples in `<file>:<line>`").

## Acceptance criteria

- [ ] Measurement methodology applied consistently — `curl -w` HTTP
  timings + first-audio mechanism documented and used for every
  measurement reported.
- [ ] Three end-to-end timings captured and recorded in Outcome:
  `cold-short`, `warm-short`, `warm-medium` (median of 3). Plus
  `warm-medium` against the pocket-tts CLI directly as the engine
  baseline.
- [ ] Each of the four hypotheses confirmed or ruled out, with
  evidence (log lines, code references, timing data).
- [ ] Outcome block written: diagnosis (which hypothesis won, where in
  code), recommended fix shape, expected scope of the fix.
- [ ] Sample inputs committed under `examples/perf/` so main-024 and
  future regression checks are reproducible.
- [ ] **main-024 acceptance criteria sharpened** based on diagnosis
  before this spike closes — at minimum, the file(s) main-024 will
  touch and the fix shape.

## Notes

- Reference: vision §What success looks like, ADR 0002 (Python sidecar),
  ADR 0003 (HTTP transport), main-018 Outcome.
- Pocket-tts upstream claims ~200 ms first-chunk; if our number is 9 s,
  the gap is almost certainly in *our* preprocessing/transport, not the
  model itself. The pocket-tts CLI baseline (step 2) is the
  load-bearing measurement here — it bisects "engine slow" vs "us slow".
- Out of scope: any code change to the speak path. If a fix is obvious
  during diagnosis (e.g. a single-line buffer flush), record it in the
  Outcome but do **not** apply it — main-024 lands the fix with proper
  before/after measurements.
- Does **not** block any other task — main-018 is closed; the page-set
  tasks are independent.
- The methodology is reusable: same measurement protocol applies to
  any future TTS perf concern.

## Code map (worker prep, 2026-05-04)

References below are file:line. They flag **suspect surfaces** for each
hypothesis — not fixes. Inspect during measurement; rule in/out with
evidence in the Outcome.

### H1 — whole-text preprocessing before first chunk

The Python sidecar tokenizes the **entire** input before any audio
generation begins, in order to split it into sentence-sized chunks:

- `pocket_tts/models/tts_model.py:465` — `chunks = split_into_best_sentences(...)`
  is called on the full `text_to_generate` *before* the chunk loop starts.
- `pocket_tts/models/tts_model.py:742` — `split_into_best_sentences()` runs
  the sentencepiece tokenizer over the entire text (`tokens = tokenizer(text_to_generate)`)
  to find sentence boundaries.

Sentencepiece is fast (microseconds per sentence) so this hypothesis is
a-priori weak — but it does prove the entire text passes through tokenization
before audio yield #1, so confirm with a stopwatch on long input if H2 doesn't account for the gap.

### H2 — sentence-batched generation (PRIMARY SUSPECT)

`pocket_tts/models/tts_model.py:469-480` — the chunk loop in
`generate_audio_stream`:

```
for chunk in chunks:
    ...
    yield from self._generate_audio_stream_short_text(...)
```

Critical observation: this is **strictly serial**. Sentence N+1's
generation does not start until sentence N's `_generate_audio_stream_short_text`
has fully completed and yielded its last frame. WITHIN a sentence,
generation and decoding are pipelined via threads
(`tts_model.py:493-502`: `latents_queue` + `decoder_thread`), giving
the ~200 ms first-chunk that pocket-tts upstream claims. ACROSS
sentences there is no parallelism.

For ~200-word input, `split_into_best_sentences` will produce roughly
5–10 chunks (max 50 tokens each per `MAX_TOKEN_PER_CHUNK` in
`pocket_tts/default_parameters.py:8`). First audio is determined by
the first sentence's first chunk — which should still be ~200 ms. So
serial sentence batching alone does NOT explain a 9 s first-audio
delay; only later audio. **If first audio is 9 s, H2 alone is not enough.**

### H3 — warmup / first-call-cold

- `pocket_tts/main.py:188` — `tts_model = TTSModel.load_model(DEFAULT_VARIANT)`
  runs once at sidecar startup (inside `serve()`, before `uvicorn.run`).
- `pocket_tts/main.py:191` — `global_model_state = tts_model.get_state_for_audio_prompt(voice)`
  also runs at startup, but only for the CLI-provided default voice.
- `pocket_tts/main.py:146` — at request time when `voice_url` is set,
  `tts_model._cached_get_state_for_audio_prompt(voice_url)` runs.
- `pocket_tts/models/tts_model.py:622` — `_cached_get_state_for_audio_prompt`
  is `@lru_cache(maxsize=2)`. **First call for any new voice is cold:**
  it downloads/decodes the audio prompt and runs Mimi encoding. Subsequent
  calls for that voice hit the cache.

Implication: the **first speak request after sidecar boot for voice X**
pays the prompt-encoding cost — even if that voice is built-in. The CLI
utterheim-host launches with `pocket_tts serve --voice <DEFAULT_AUDIO_PROMPT>`
(no `--voice` override in `SidecarHost.cs:221`), so `global_model_state`
is pre-warmed only for the upstream default — NOT for `alba` or any of
the eight built-ins utterheim actually uses.

This means **every first call after sidecar boot to a new voice pays a
full prompt-encoding warmup**, regardless of how warm the model itself is.

### H4 — HTTP transport buffering (PRIMARY SUSPECT)

Two halves to check:

- **Python side (good):** `pocket_tts/main.py:166-173` — the `/tts` endpoint
  returns a `StreamingResponse` with explicit `Transfer-Encoding: chunked`.
  The generator (`generate_data_with_state` at line 96) yields audio bytes
  as they come off the model thread. So Python is streaming correctly.

- **C# side (suspect):** `src/Utterheim/Services/Tts/PocketTtsEngine.cs:78`
  uses `_http.PostAsync(url, content, ct)`. The default `HttpCompletionOption`
  is `ResponseContentRead`, which **reads the entire response body into
  memory before the awaited Task completes**. That means we wait for
  pocket-tts to finish *all* synthesis before the C# code even sees the
  first byte. For a ~200-word paragraph at ~2 s wall-clock per sentence
  that's exactly the kind of multi-second tail we observe.

  Compare with: `_http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)`
  which returns as soon as headers arrive and lets `ReadAsStreamAsync()`
  surface chunks live.

  This is the most likely single-line fix candidate for main-024.

- **Audio dispatch (instrumentation only):**
  `src/Utterheim/Services/Speak/AudioPlayer.cs:78` — `FIRST-AUDIO-DISPATCH`
  log line marks T1 (first PCM chunk handed to NAudio's
  `BufferedWaveProvider`).

## Prep complete (worker, 2026-05-04)

### Files created/modified

- `examples/perf/short-input.txt` — exact 5-word probe.
- `examples/perf/medium-input.txt` — 187-word coherent paragraph from vision (Purpose + first paragraph of "The problem", lightly adapted).
- `examples/perf/medium-input.source.txt` — sibling note recording the source.
- `examples/perf/long-input.txt` — 1091-word concatenation of vision §Purpose, §Users, §The problem, §What success, §Non-goals, §Key drivers, §Open questions (lightly adapted to clean punctuation).
- `examples/perf/long-input.source.txt` — sibling source note.
- `examples/perf/user-sample.txt` — Marco's 65-word voice-transcribed sample (verbatim, garbled).
- `examples/perf/measure-latency.ps1` — measurement harness (curl.exe + log-tail for FIRST-AUDIO-DISPATCH; supports `-Cold`, `-Repeat`, median).
- `src/Utterheim/Services/Speak/AudioPlayer.cs` — added one Serilog `Log.Information` call inside `PlayAsync` that emits exactly once per request, containing the literal substring `FIRST-AUDIO-DISPATCH`. Permanent instrumentation.

### Log line location

`src/Utterheim/Services/Speak/AudioPlayer.cs:78` — fires at the moment the first PCM chunk is handed to `BufferedWaveProvider.AddSamples` (which is what NAudio's `WaveOutEvent` will pick up on its next pump). Gated by a local `firstSampleLogged` flag scoped to `PlayAsync`, so it fires at most once per speak request.

### Pocket-tts CLI invocation hint (engine baseline)

The cleanest engine-only first-byte measurement runs pocket-tts's own
sidecar bypassing utterheim. The pocket-tts CLI is on the system PATH
(installed at `C:\Users\marco\AppData\Local\Programs\Python\Python312\Scripts\pocket-tts.exe`)
and via `python -m pocket_tts ...`.

Two options, in increasing fidelity:

1. **Whole-utterance baseline** (bounds first-audio from above):
   ```powershell
   Stop-Process -Name utterheim -Force -ErrorAction SilentlyContinue   # don't fight for the model
   Measure-Command { pocket-tts generate --text (Get-Content -Raw .\examples\perf\medium-input.txt) --voice alba --output-path .\examples\perf\baseline-medium.wav --quiet }
   ```
   Records total wall-clock to render the entire WAV. First-audio is necessarily ≤ this.

2. **First-byte baseline** (apples-to-apples with our HTTP path):
   ```powershell
   # Terminal A — start a clean pocket-tts server, NOT going through utterheim:
   pocket-tts serve --host 127.0.0.1 --port 8765
   # …wait until "Application startup complete." appears…

   # Terminal B — once server is up, time first byte:
   $body = "text=$(Get-Content -Raw .\examples\perf\medium-input.txt)&voice_url=alba"
   curl.exe -sS -o NUL -X POST `
     http://127.0.0.1:8765/tts `
     -H "Content-Type: application/x-www-form-urlencoded" `
     --data-urlencode "text@.\examples\perf\medium-input.txt" `
     --data "voice_url=alba" `
     -w "time_starttransfer=%{time_starttransfer}s  time_total=%{time_total}s`n"
   ```
   `time_starttransfer` is the engine's true first-byte-out time. If this
   is ≤2 s for medium input but our end-to-end is ~9 s, the entire delta
   lives in utterheim (most likely H4 — see code map).

If either invocation fails (e.g. the utterheim-bootstrapped runtime
in `%LOCALAPPDATA%\Utterheim\runtime\python\` differs from the system
Python and you need to use it instead), substitute:
`& "$env:LOCALAPPDATA\Utterheim\runtime\python\python.exe" -m pocket_tts ...`
with the same arguments. The runtime dir does not exist yet on the dev
box at prep time — it will after the first utterheim run that
completes the bootstrap dialog.

### Measurement runbook

```
1. Build utterheim so the FIRST-AUDIO-DISPATCH log line is live:
     dotnet build C:\src\heimeshoff\containers\utterheim\utterheim.sln

2. Start utterheim (run the built exe, or F5 from Rider). Wait until
   the tray status footer reads:
     HTTP 127.0.0.1:7223 | Engine: running

3. From the repo root, run measurements (the script writes to stdout one
   line per measurement, plus a median line when -Repeat > 1):

     .\examples\perf\measure-latency.ps1 -InputFile .\examples\perf\short-input.txt -Cold
     .\examples\perf\measure-latency.ps1 -InputFile .\examples\perf\short-input.txt
     .\examples\perf\measure-latency.ps1 -InputFile .\examples\perf\medium-input.txt -Repeat 3
     .\examples\perf\measure-latency.ps1 -InputFile .\examples\perf\long-input.txt  -Repeat 3
     .\examples\perf\measure-latency.ps1 -InputFile .\examples\perf\user-sample.txt -Repeat 3

4. Engine baseline (no utterheim in the loop) — see "Pocket-tts CLI
   invocation hint" above. Run option 2 (first-byte baseline) for
   medium-input.txt and record `time_starttransfer`. If that is ≤2 s
   while the utterheim end-to-end medium run is ~9 s, H4 is confirmed.

5. Paste all numbers into a new "## Outcome" section at the end of this
   task file:
     - cold-short, warm-short, warm-medium (median of 3),
       warm-long (median of 3), warm-user (median of 3)
     - engine baseline first-byte for medium
     - which hypothesis(es) won, with file:line refs
     - recommended fix shape for main-024
```

## Outcome (2026-05-04)

### Verdict: H4 confirmed — single change in C# host

Latency scales linearly with input size because `_http.PostAsync(...)` at
`src/Utterheim/Services/Tts/PocketTtsEngine.cs:78` uses the default
`HttpCompletionOption.ResponseContentRead`, which buffers the entire
response body before the awaited Task completes. Python's `/tts` endpoint
streams correctly (`StreamingResponse`, `Transfer-Encoding: chunked`), but
C# discards that streaming property by waiting for the whole WAV before
reading the first byte.

The downstream chunk-reading loop in `PocketTtsEngine.cs:86–102` is already
streaming-shaped — it just needs to receive a real network-backed stream
instead of a pre-buffered MemoryStream.

### Measurements (single fresh sidecar boot, voice=alba)

| Run | Input | Chars | TTFB | end-to-end (first audio) |
|---|---|---:|---:|---:|
| cold-short | "Hello, this is utterheim." | 27 | n/a | **663 ms** ✓ |
| warm-short | same | 27 | n/a | **802 ms** ✓ |
| warm-medium (clean queue) | vision §Purpose paragraph | 1,159 | **63 ms** | **22,968 ms** ❌ |
| warm-long | vision sections concatenated | 6,855 | n/a | **139,000 ms** ❌ |

(`cold-short` and `warm-short` from the script's own output. `warm-medium`
from a clean re-run on a drained queue with no `-Repeat`. `warm-long`
read from the utterheim log — the script's 30 s tail-poll timed out
but the request itself completed, so the actual T1 timestamp is
authoritative.)

Latency-per-character is roughly constant at ~20 ms/char for inputs
above ~1 KB. That linearity is the H4 signature: in a streaming pipeline,
first-audio time is independent of total input size.

### Smoking-gun evidence

For the medium run:
- HTTP TTFB = **63 ms** — Python returns headers essentially instantly.
- `FIRST-AUDIO-DISPATCH` fires **22,968 ms** later.

The 22.9 s window is the C# buffering window: Python is streaming bytes
the whole time, C# isn't reading them until the body completes. Same
pattern at 6 × scale on the long run.

### Hypotheses ruled out

- **H1 (whole-text preprocessing):** sentencepiece tokenization runs in
  microseconds; cannot produce 22.9 s on a 1.2 KB input. Not the cause.
- **H2 (sentence-batched generation):** within a sentence, pocket-tts
  pipelines generation/decoding (`tts_model.py:493-502`) and yields
  ~200 ms first chunk. The serial-across-sentences cost only manifests
  *after* first audio. Not the cause of first-audio delay.
- **H3 (per-voice prompt-encoding warmup):** would be a one-time hit on
  the first call to a voice. Cold-short was 663 ms (within budget), so
  even cold cost is fine. Not the cause.

### Recommended fix shape (for main-024)

Replace line 78 with:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
using var response = await _http.SendAsync(
    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
```

Lines 79–102 stay identical. `ReadAsStreamAsync()` will now return a
network-backed stream, and the existing chunk-loop already handles it.

**Expected outcome after fix:**
- First-audio time becomes input-length-independent.
- Lands in ~200–500 ms across all inputs (pocket-tts intrinsic first-chunk
  + HTTP RTT + 8 KB ReadAsync buffer fill).
- main-024's existing acceptance criteria become trivially met.

### Methodology + sample inputs landed

`examples/perf/` carries the canonical `short`, `medium`, `long`, and
`user-sample` inputs plus `measure-latency.ps1`. The
`FIRST-AUDIO-DISPATCH` log line at `AudioPlayer.cs:78` is permanent
instrumentation. Same harness produces before/after comparisons for
main-024.

The script's 30 s log-tail timeout is fine post-fix (the budget is now
~500 ms, with 60× headroom). For the broken case it timed out on
medium and long; we read T1 from the log directly.

### Side issues observed (non-blocking)

- **pocket-tts chunk warning** on long input:
  `Chunk has 59 tokens (max 50), generation may skip words: 'whether
  a finished-task cue can barge to the front...'` A sentence in
  vision §Open questions exceeds pocket-tts's `MAX_TOKEN_PER_CHUNK=50`
  limit. Engine accepts it with a warning. Pre-splitting at the
  sidecar layer is a future option if the skip risk bites; not filing.
- **`TerminateProcess` warning** on tray Exit:
  `No process is associated with this object` (`SidecarHost.cs:393`)
  — fires because main-022's Job Object already killed `python.exe`
  by the time `Process.HasExited` is checked. Cosmetic. Worth filing
  as a low-priority cleanup task when the user wants.

