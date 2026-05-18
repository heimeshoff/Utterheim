---
topic: Kyutai pocket-tts German language support — model variants, runtime selection, voice cloning, and plugin integration
date: 2026-05-18
requested_by: user
related_tasks: [main-035, main-036, main-037, main-038, main-039, main-040, main-041, main-042, main-043]
---

# Research: Kyutai pocket-tts German language support

## Question

The 2026-05-01 research flagged pocket-tts as English-only. The user reports
seeing on HuggingFace that German is now supported. Is this real? If so:

- Where does German live (separate model id? new revision? new kwarg)?
- How is the language selected at runtime — per instance or per call?
- Does voice cloning work for German source audio, and are `.safetensors`
  voice profiles re-usable across languages?
- What breaks vs pocket-tts 2.0.0 for the symbols the existing sidecar imports
  (`pocket_tts.main.web_app`, `tts_model`, `generate_data_with_state`,
  `export_model_state`, `_import_model_state`)?
- What's a clean per-prompt interface for a Claude Code plugin?

## Summary

- **German is real and shipped.** Kyutai released **pocket-tts 2.0.0 on
  2026-04-21** (first multilingual drop) and **2.1.0 on 2026-05-04** (the
  "Pocket TTS now supports six languages" announcement). German is one of six
  languages alongside English, French, Italian, Spanish, Portuguese. [1][2][3][7]
- **No new HuggingFace repo id.** The single `kyutai/pocket-tts` repo gained a
  `languages/` subdirectory (added ~mid-April 2026) holding per-language model
  weights. You stay on `kyutai/pocket-tts` and select language at load time. [4]
- **Language is per model instance, not per call.** `TTSModel.load_model(language=...)`
  binds the language for the lifetime of the instance; `generate_audio` /
  `generate_audio_stream` take no language argument. To switch language at
  runtime you must load a second model. [5][6][8]
- **Each model is ~135 MB resident RAM** (100M params), so keeping `en` + `de`
  both resident in one Python process is comfortable — call it ~300 MB total.
  This makes a "two pre-loaded models, route per request" pattern realistic for
  the tray app. [9]
- **German has two variants**: the default 6-layer distilled `german`, and
  `german_24l` — the undistilled 24-layer preview, "higher quality but slower",
  same shape for `french_24l`, `italian_24l`, `spanish_24l`, `portuguese_24l`.
  Kyutai explicitly asks for bug reports comparing the two. [1][5][6]
- **Voice cloning works for German**, but Kyutai recommends a German voice
  prompt rather than reusing an English clone — and added a new built-in
  `juergen` voice for German to avoid English-accented defaults. The built-in
  English voices (alba, marius, etc.) technically work but will carry an
  English accent into German output. [1][10][11]
- **`.safetensors` voice profiles are not explicitly language-tagged.** The
  README doesn't say they're language-bound, and the underlying KV-cache is the
  same shape across the 6-layer distilled models — but Kyutai recommends
  matching prompt language to model language, and there's no documented
  guarantee a profile exported on `english` works cleanly on `german`. Treat
  voice profiles as soft-coupled to the model variant they were exported with.
  Single-source caveat; the docs are silent on cross-language portability. [10]
- **The sidecar's import surface survives.** `pocket_tts.main.web_app`,
  `tts_model`, and `generate_data_with_state` are all still defined in 2.1.0's
  `pocket_tts/main.py`. `export_model_state` is still exposed; `import_model_state`
  is folded into `get_state_for_audio_prompt()` — pass a `.safetensors` path
  and it loads directly. The sidecar's existing `TypeError` fallback around
  `language=` is now redundant in practice (2.0.0+ accepts it) but harmless. [5][8][12]
- **Streaming/latency for German**: Kyutai's blog headline says "fast enough
  to run real-time without a GPU" for all six languages — i.e. the distilled
  `german` matches the English RTF. No separate German benchmark numbers
  published. The `german_24l` is explicitly slower (preview, not for
  production). [2][3]

## Findings

### 1. Is German support real, and where does it live?

Yes. Two confirming releases on PyPI:

- `pocket-tts 2.0.0` — 2026-04-21 — first multilingual drop. Introduced the
  `--language` CLI flag and `language=` kwarg on `TTSModel.load_model()`. [3][7]
- `pocket-tts 2.1.0` — 2026-05-04 — coincides with the Kyutai blog post
  "Pocket TTS now supports six languages". Adds default voices per language
  (issue #166), fixes device compatibility for non-CPU hardware, fixes voice
  cloning under quantization, and rephrases the default German sentence. [3][7][11]

The Kyutai launch tweet says: *"Pocket TTS goes multilingual! Now you can use
our 100M-parameter models to generate speech in six languages, fast enough to
run real-time without a GPU. We also improved the quality of the English model
while keeping the same size."* [2]

**HuggingFace layout**: there is **no new repo id**. The single
`kyutai/pocket-tts` repo (and the cloning-disabled sibling
`kyutai/pocket-tts-without-voice-cloning`) gained a `languages/` subdirectory
holding per-language weights, added ~14 days before this research (i.e. early
May 2026). The English weights from January still sit at the repo root for
backwards compatibility, and the `language=` kwarg selects which subdirectory
to pull from. There is no separate `kyutai/pocket-tts-de` repo. [4]

The prior 2026-05-01 research's "English-only" verdict was correct for the
weights that existed on the HF main branch *at that snapshot*; the multilingual
weights landed mid-April with 2.0.0 but the user-facing HuggingFace model card
text wasn't fully updated when I re-fetched it for this research — it still
says "English only at the moment" in the rendered card. That's stale; the
authoritative current sources are the GitHub README, the May 4 blog post, and
PyPI 2.1.0. [1][2][8]

### 2. Supported language list

Six languages, with two model lineages per non-English language: [1][5][6]

| Language | Default model name | Larger variant | Built-in voice |
|---|---|---|---|
| English (default) | `english` (alias `english_2026-04`) | — (also `english_2026-01` legacy) | 18+ voices (alba, marius, javert, jean, fantine, cosette, eponine, azelma, anna, eve, vera, …) |
| French | `french` | `french_24l` | estelle |
| German | `german` | `german_24l` | juergen |
| Italian | `italian` | `italian_24l` | giovanni |
| Spanish | `spanish` | `spanish_24l` | lola |
| Portuguese | `portuguese` | `portuguese_24l` | rafael |

Caveats Kyutai themselves call out:

- The `*_24l` undistilled variants are **preview only** — "bigger 24-layer
  variants that are higher quality but slower… not distilled yet, here only as
  preview". For German specifically, Kyutai asks users to report bugs that
  appear in `german` (distilled) but not in `german_24l` (undistilled),
  signalling that the distillation step is the most likely source of German
  quality regressions. [1][5]
- Issue #166 acknowledges that running the German model with default English
  voices produces "audio with an English accent" — they added the `juergen`
  default voice in 2.1.0 to fix this on first run. [11]
- The English model itself was re-trained alongside the multilingual drop and
  Kyutai claims a quality improvement at the same size. The 2026-01 weights
  are still available as `english_2026-01` for reproducibility. [2][5]

No published quality / MOS / blind-test numbers for German vs other open
multilingual TTS systems yet. Single-vendor claim only.

### 3. Runtime language selection — per-instance, not per-call

This is the most important architectural finding for the tray app.

**CLI**: `--language` is passed to `generate`, `export-voice`, and `serve`. [1][13]

```
pocket-tts serve --language german
pocket-tts generate --language german_24l --voice juergen --text "Guten Tag"
pocket-tts export-voice clip.wav voice.safetensors --language german
```

**Python**: full signature from the API reference: [5]

```python
TTSModel.load_model(
    language=None,            # e.g. "german", "german_24l", "english", ...
    config=None,
    temp=0.7,
    lsd_decode_steps=1,
    noise_clamp=None,
    eos_threshold=-4.0,
    quantize=False,
)
```

The generation methods take **no** language argument: [5]

```python
generate_audio(model_state, text_to_generate, frames_after_eos=None, copy_state=True)
generate_audio_stream(model_state, text_to_generate, frames_after_eos=None, copy_state=True)
```

So **language is bound to the model instance**. There is no per-call language
switch. To support per-prompt language routing in the tray app, you must:

1. Load one resident `TTSModel` per language you want to serve, OR
2. Reload the model on language change (slow — defeats the "warm sidecar"
   pattern), OR
3. Pick a single default language at sidecar start and accept the constraint.

**Memory budget for option 1**: third-party measurement puts a single resident
model at ~135 MB RAM. Two models (en + de) is ~270 MB; six languages would be
~810 MB. For a Windows tray app on a modern dev box, en + de in the same
process is well within budget. Note: 135 MB is a **single-source community
number** (DeepWiki / Medium walkthrough), not a Kyutai-published figure. The
underlying 100M-param FP32 weights are ~400 MB on disk and PyTorch's resident
working-set is typically smaller than that thanks to mmap'd safetensors; 135 MB
is plausible but should be validated on the user's actual hardware before
committing to the multi-model design. [9]

The existing utterheim sidecar's `serve` command (`PythonSidecar/utterheim_sidecar/main.py`
lines 280–322) already passes `language=` to `TTSModel.load_model` with a
`TypeError` fallback. On pocket-tts 2.1.0 the fallback path is dead code
(`language=` is always accepted), but it remains a safe defensive measure if
you ever pin to a pre-2.0 release. The sidecar today only loads **one** model
per process — supporting per-prompt language switching requires extending
`serve` to either accept a list of languages and load multiple `TTSModel`
instances, or to fork additional sidecar subprocesses.

### 4. Voice cloning for German

The cloning API is unchanged: `get_state_for_audio_prompt(audio_conditioning, truncate=False)`
where the conditioning can be a path to a WAV, a HuggingFace `hf://...` voice
path, a built-in voice name, or a `.safetensors` path. No new language kwarg
on this function. [5]

**Does the existing audio-prompt encoder work with German source audio?** Yes,
implicitly — the Mimi audio encoder is shared across all language models, and
the README documents the same cloning workflow regardless of language. The
recommendation is to "use a voice prompt that corresponds to your target
language" — i.e. use a German clip when generating German output — but the
mechanism is the same encoder. [1][10]

**Are exported `.safetensors` voice profiles language-tagged?** The
documentation is **silent on this**. The export command's `--language` flag
determines which model the prompt is encoded against, which strongly suggests
the resulting KV-cache is shape-compatible with that specific model variant.
The export_voice docs only say the file contains "the kvcache in safetensors
format" with no portability guarantee across languages. [10][13]

In practice, the safest assumption is:

- A `.safetensors` exported with `--language german` is meant to be loaded
  against a `german` model.
- The 6-layer distilled models likely share architecture (so en/de/es/fr/it/pt
  profiles are probably tensor-shape-compatible), but the *acoustic* match
  between a prompt encoded against one model and generation by another is
  undocumented and may degrade silently.
- The `*_24l` undistilled variants are a different architecture (24 layers vs
  6) and KV-caches almost certainly **don't** transfer between distilled and
  24l.

This is a documentation gap, not a confirmed restriction. Mark as a
**single-source / untested** claim — the user should run a small experiment
(export a voice on `german`, load it against `english`, listen) before
designing the voice-library schema around cross-language portability.

**Sample-length recommendations**: unchanged from the 2026-05-01 research —
the README only states "we recommend cleaning the sample before using it" and
the export_voice command notes "only the first 30 seconds of the audio file
will be processed". No German-specific minimum. The 5-second Kyutai claim and
the 10–20-second community sweet spot from the prior research still apply. [1][10]

**Do English built-in voices work for German output?** Mechanically yes — the
KV-cache shape is the same — but issue #166 explicitly calls out that this
produces **English-accented German**, which is why 2.1.0 added `juergen` as
the German default. So: technically usable, but Kyutai themselves recommend
using a language-matched voice. [11]

### 5. Streaming and latency for German

Kyutai's headline is "fast enough to run real-time without a GPU" applied to
all six distilled language models. The 100M-param size is unchanged across
languages, so the English numbers from the 2026-05-01 research (~200 ms
first-chunk, ~6× real-time on MacBook Air M4 with 2 CPU cores) should
generalise to German. **Kyutai have not published a separate German
benchmark.** [1][2]

The `german_24l` undistilled variant is explicitly slower — by how much is
not quantified, but a 24-layer transformer vs a 6-layer one is a ~4× compute
increase in the language-model stack, so expect roughly 4× the real-time
factor (still likely real-time on a modern CPU, but with less headroom and
higher first-chunk latency). Single-vendor framing, no third-party benchmarks. [1][5]

### 6. Breaking changes vs pocket-tts 2.0.0 — sidecar import surface

The sidecar imports five symbols from pocket-tts. Status as of 2.1.0: [8][12]

| Symbol | 2.1.0 status | Notes |
|---|---|---|
| `pocket_tts.main.web_app` | **present** | Still the FastAPI app at `pocket_tts/main.py` line 54; CORS middleware unchanged. |
| `pocket_tts.main.tts_model` | **present** | Module-level `tts_model: TTSModel \| None = None` at line 51 — the slot the sidecar `setattr`'s into. |
| `pocket_tts.main.generate_data_with_state` | **present** | Defined at line 121, signature `(text, model_state)` — yields WAV-framed bytes. The sidecar's defensive check for absence (line 250+) is still appropriate but currently always finds it. |
| `pocket_tts.export_model_state` | **present** | Documented in the Python API reference. |
| `pocket_tts.models.tts_model._import_model_state` | **superseded** | The Python API docs no longer list `import_model_state` as a top-level helper. Loading a `.safetensors` profile is now done by passing the path directly to `get_state_for_audio_prompt("path/to/voice.safetensors")`, which detects the extension and calls `load_file` internally. The private `_import_model_state` may still exist in `pocket_tts/models/tts_model.py` (it's the implementation `get_state_for_audio_prompt` delegates to), but treating it as private API was always risky — the public path is the one to use. [5][12] |

`TTSModel.load_model()` gained the documented signature in question 3 above.
The 2.0.0 release also introduced quantization (`--quantize` / `quantize=True`),
and 2.1.0 fixed a voice-cloning bug under quantization — relevant if utterheim
ever turns it on. [3][7]

**No removals** of the symbols the sidecar imports. The sidecar's `TypeError`
fallback for `language=` is now dead code in practice (2.0.0+ always accepts
the kwarg), but leaving it in costs nothing.

### 7. Per-prompt language selection — design patterns from other systems

How other multilingual TTS systems expose language selection over an HTTP API:

- **Chatterbox Multilingual (resemble-ai)**: a `language_id` field in the request
  payload, e.g. `language_id="de"`. The multilingual model handles all 22+
  languages in one resident instance — language is genuinely a per-request
  parameter, not a load-time bind. [14]
- **Coqui XTTS-v2**: similar pattern — single multilingual checkpoint, `language="de"`
  passed alongside `text` and `speaker_wav` on each call. [14]
- **ElevenLabs**: language is mostly inferred from text by their multilingual
  models (Eleven v3, Multilingual v2); you can optionally pin a `language_code`
  on the request. No SSML required. [15]
- **Azure Speech / Google Cloud TTS**: SSML `xml:lang` attribute on the text
  element, or a per-request `voice.languageCode`. SSML is the legacy
  enterprise pattern.
- **Voice-profile-carries-language**: some systems (Chatterbox-Server, Coqui
  setups) attach a default language to each saved voice profile, so the API
  can default the language from the voice if the request doesn't specify one.

**For utterheim/pocket-tts specifically**, the runtime constraint is decisive:
pocket-tts is **load-time-bound**, unlike Chatterbox-multilingual or XTTS-v2.
So the plugin's HTTP API can expose a `language` field per request, but the
sidecar has to route that request to a different resident model (or reload,
or reject if the language isn't pre-loaded). This is a sidecar-shape decision
rather than a wire-format decision.

Reasonable interface options the modeling skill should choose between:

1. **`language` field in the speak-request body** — e.g.
   `{"text": "Guten Tag", "voice": "juergen", "language": "de"}`. Sidecar
   maps `de`→`german` (or `german_24l` per config) and routes to the
   matching resident model. Voice and language are independent fields.
2. **Voice-profile-carries-language** — each saved voice profile has a
   `language` attribute; the request only sends `{"text": ..., "voice": ...}`
   and language is inferred. Simpler API; requires the voice library to gain
   a language column.
3. **Auto-detect from text** — sidecar runs a fast language detector
   (`langdetect`, `lingua-py`) on each request. Zero config for the plugin,
   but unpredictable and a new dependency.
4. **SSML `xml:lang`** — overkill for a personal tray app; the plugin would
   need an SSML builder.

The decision-relevant trade-offs: (a) is voice-language coupled or
orthogonal? (b) does the plugin always know the language up front? (c) how
many resident models do we want to pay for? These are model-skill questions,
not researcher answers — listed here so the modeling skill can pick.

## Sources

1. [kyutai-labs/pocket-tts README (GitHub)](https://github.com/kyutai-labs/pocket-tts) — current multilingual README. Lists six languages, `--language` flag, 24l variants, per-language built-in voices. Primary source.
2. [Pocket TTS now supports six languages (Kyutai blog, 2026-05-04)](https://kyutai.org/blog/2026-05-04-pocket-tts-multilingual) — multilingual launch announcement. Blog body didn't fully render via WebFetch; headline + tweet thread captured.
3. [pocket-tts on PyPI](https://pypi.org/project/pocket-tts/) — release history: 2.1.0 (2026-05-04), 2.0.0 (2026-04-21), 1.1.x (Feb 2026), 1.0.x (Jan 2026).
4. [kyutai/pocket-tts file tree (HuggingFace)](https://huggingface.co/kyutai/pocket-tts/tree/main) — shows the `languages/` directory added ~14 days ago, no separate `kyutai/pocket-tts-de` repo. English weights still at repo root.
5. [Python API Documentation (Pocket TTS site)](https://kyutai-labs.github.io/pocket-tts/API%20Reference/python-api/) — authoritative `TTSModel.load_model(language=None, config=None, temp=0.7, lsd_decode_steps=1, noise_clamp=None, eos_threshold=-4.0, quantize=False)` signature; full list of valid language names: `english_2026-01`, `english_2026-04`, `english`, `french_24l`, `german_24l`, `portuguese_24l`, `italian_24l`, `spanish_24l`.
6. [kyutai-labs/pocket-tts release notes — v2.0.0 / v2.1.0 (GitHub)](https://github.com/kyutai-labs/pocket-tts/releases) — v2.0.0 introduced `--language` and `language=` kwarg, quantization. v2.1.0 added per-language default voices, fixed cloning under quantization, fixed non-CPU device compatibility.
7. [kyutai-labs/pocket-tts v2.1.0 (newreleases.io)](https://newreleases.io/project/github/kyutai-labs/pocket-tts/release/v2.1.0) — cross-check on release date 2026-05-04.
8. [kyutai/pocket-tts model card (HuggingFace)](https://huggingface.co/kyutai/pocket-tts) — note: as of 2026-05-18 the rendered card text still says "English only at the moment"; this is stale relative to README/PyPI/blog. Vendor source, but out of date — do not rely on it for language support.
9. [Kyutai Pocket TTS analysis (Medium, Susan Cook, 2026)](https://medium.com/@cooksusan482/kyutai-pocket-tts-100m-parameter-that-runs-on-your-cpu-6cae1fd812bf) and [DeepWiki: pocket-tts](https://deepwiki.com/kyutai-labs/pocket-tts) — community walkthroughs. RAM ~135 MB per resident model is a third-party measurement, not a Kyutai number — single-source claim.
10. [Export Voice command (kyutai-labs.github.io)](https://kyutai-labs.github.io/pocket-tts/CLI%20Commands/export_voice/) and [export_voice.md (GitHub)](https://github.com/kyutai-labs/pocket-tts/blob/main/docs/CLI%20Commands/export_voice.md) — export-voice signature, `--language` flag, "only first 30s processed". Silent on whether profiles are cross-language portable.
11. [Issue #166: Add new default voices for each new language to avoid English accents](https://github.com/kyutai-labs/pocket-tts/issues/166) — confirms English voices produce English-accented German output; closed by adding `juergen` (de), `estelle` (fr), etc. as language defaults in 2.1.0.
12. `pocket_tts/main.py` symbol extraction via WebFetch of the GitHub blob — confirms `web_app` (line 54), `tts_model` (line 51), `generate_data_with_state` (line 121), and the `serve(host, port, reload, language, config, quantize)` signature still match what the utterheim sidecar imports. Cross-referenced against `src/Utterheim/PythonSidecar/utterheim_sidecar/main.py` lines 250–322.
13. [pocket-tts/docs/CLI Commands/export_voice.md (GitHub)](https://github.com/kyutai-labs/pocket-tts/blob/main/docs/CLI%20Commands/export_voice.md) — full export-voice command docs including the language argument's accepted values.
14. [Chatterbox TTS API multilingual docs](https://chatterboxtts.com/docs/multilingual) — `language_id` request parameter pattern, single multilingual model serving all 22+ languages. Reference for per-call language design (contrast to pocket-tts's per-instance binding).
15. [ElevenLabs Cheat Sheet 2026 (Webfuse)](https://www.webfuse.com/elevenlabs-cheat-sheet) — auto-detect-from-text pattern for multilingual TTS APIs. Third-party summary.
16. [Issue #118: Official announcement — More languages for Pocket-TTS](https://github.com/kyutai-labs/pocket-tts/issues/118) — original 2026-02-10 announcement listing the five planned non-English languages (es, fr, de, pt, it). Useful for the timeline.
17. [kyutai on X — "Pocket TTS goes multilingual"](https://x.com/kyutai_labs/status/2051317316713894368) — first-party launch tweet for 2.1.0, headline real-time-without-GPU claim across all six languages. Vendor-marketing source.

## Open questions

- **Cross-language `.safetensors` portability.** Documentation is silent. Most
  likely: 6-layer distilled profiles are tensor-shape-compatible across en/de/
  es/fr/it/pt but acoustically degraded when cross-loaded; 24l profiles are a
  separate shape. Needs a 10-minute experiment by the worker before the voice
  library schema commits to a single-language-per-profile assumption.
- **German-specific quality benchmarks.** Kyutai have not published MOS, WER,
  or blind-test numbers comparing the German distilled model against XTTS-v2
  / Chatterbox / ElevenLabs. The `german_24l` preview's quality delta over
  `german` is also unquantified — only "higher quality but slower". User
  should listen to both before picking the production default.
- **Resident-RAM measurement on Windows.** The ~135 MB figure is a single
  community measurement on macOS / Linux. Validate with a memory profiler on
  the user's actual Windows machine before committing to multi-model resident
  pattern.
- **Whether the modeling skill should expose `german_24l` as a user-selectable
  variant.** If yes, the voice library / settings UI need a "model variant"
  axis on top of "language". This is a UX decision punted to `model`.
- **Per-prompt interface choice for the Claude plugin.** Four viable patterns
  listed in section 7; picking between them depends on whether voices are
  meant to be language-bound (voice-profile-carries-language) or orthogonal
  (separate `language` field). Not a researcher decision.
