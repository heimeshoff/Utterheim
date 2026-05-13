---
topic: Kyutai Pocket TTS for a local Windows tray TTS service with sample-based voice cloning
date: 2026-05-01
requested_by: user
related_tasks: []
---

# Research: Kyutai Pocket TTS

## Question

Is "Pocket TTS" the right Kyutai project for a personal Windows tray TTS service that
streams synthesized audio and supports voice cloning from a short sample? What's the
exact name, license, hardware story, latency, Windows story, and API surface?

## Summary

- The project is real and the name is **`pocket-tts`** (lowercase, hyphenated). The
  official repo is `kyutai-labs/pocket-tts`, the PyPI package is `pocket-tts`, and the
  HuggingFace model is `kyutai/pocket-tts`. It's a separate, smaller sibling of the
  earlier 1.6B-parameter "Kyutai TTS" model. [1][2][3]
- It's a great fit for the stated use case: 100M params, CPU-only, ~6x real-time on a
  MacBook Air M4, ~200ms first-chunk latency, native streaming, voice cloning from a
  short user-supplied WAV that gets cached as a `.safetensors` "voice profile". [1][3][4]
- Licenses are permissive: code MIT, weights CC-BY-4.0. Personal and commercial use are
  both allowed; the model card forbids impersonation without consent / fraud /
  misinformation. [3]
- There are real caveats for the user's plan: **English-only at launch** (more languages
  "planned"), and the package is pure Python on PyPI but has not been explicitly tested
  / documented for Windows by Kyutai — the wheel tag (`py3-none-any`) suggests it should
  install fine, and PyTorch 2.5+ on Windows is well-supported, but the docs only call
  out macOS performance numbers. [3][5]
- "Official version" almost certainly refers to `kyutai-labs/pocket-tts` itself — there
  are community ports (sherpa-onnx ONNX build, PocketTTS.cpp, TorchSharp, Rust/Candle,
  WASM). The Python package on PyPI from Kyutai is the canonical "official" one. [4]
- Best obvious alternative if Pocket TTS feels limiting: **Chatterbox** (multilingual,
  MIT, ~5s reference clip, sub-200ms latency on GPU, beats ElevenLabs in blind tests per
  Resemble) or **F5-TTS** (resource-efficient, ~3GB VRAM, very expressive). XTTS-v2 is
  popular but its license restricts commercial use. [6]

## Findings

### 1. Exact project name and repository

The user said "Pocket TTS / pocket-tts". That **is** the actual project name. Kyutai's
own blog announced "Pocket TTS: a high-quality TTS with voice cloning that runs on CPU"
on 2026-01-13. [2]

- GitHub: `https://github.com/kyutai-labs/pocket-tts` — repo tagline: "A TTS that fits
  in your CPU (and pocket)". [1]
- HuggingFace: `https://huggingface.co/kyutai/pocket-tts` (and a sibling
  `kyutai/pocket-tts-without-voice-cloning`). [3]
- PyPI: `https://pypi.org/project/pocket-tts/` — current version **2.0.0** released
  2026-04-21. [5]
- Project site: `https://kyutai-labs.github.io/pocket-tts/` (browser/WASM demo). [4]

It is **not** the same as:
- **Kyutai TTS 1.6B** (`kyutai/tts-1.6b-en_fr`, July 2025) — much larger, server-class,
  ships with a curated voice library, lives in the `delayed-streams-modeling` repo. [4][7]
- **Moshi** — the speech-to-speech LLM.
- **Mimi** — the neural audio codec used internally (the audio prompt is encoded via
  Mimi to produce the "voice state"). [8]
- **Hibiki** — Kyutai's translation model.
- **Unmute** — an LLM-wrapping system that uses Kyutai TTS + STT.

The "official version" the user asked about: the canonical, Kyutai-published Python
package is `pip install pocket-tts`. Community ports exist (PocketTTS.cpp, TorchSharp,
sherpa-onnx ONNX build, Rust/Candle, WASM) — these are useful but not first-party. [1]

### 2. Voice cloning workflow

Pocket TTS supports **sample-based zero-shot voice cloning** — exactly the workflow the
user wants. There is no fine-tuning step; you hand it a WAV at inference time. [1][2]

How long a clip? Sources disagree mildly:
- Kyutai's own X/Twitter post says **5 seconds** is enough to capture "voice color,
  emotion, accent, and acoustic conditions (reverb, mic quality)". [9]
- Community write-ups quote **~20 seconds** as a working number. [10]
- The README only says "we recommend cleaning the sample before using it" because
  artefacts in the source clip (noise, reverb) get reproduced in the output. [1]

A reasonable default is a 10–20 second clean clip; 5s is the documented minimum.

Voice profile storage: a clip is encoded once via the **Mimi** audio encoder into a
"voice state" (a kvcache), and that state can be exported to a `.safetensors` file for
fast subsequent loads ("just reading the kvcache from disk"). This is exactly the
"drop a clip → reuse forever" pattern the user described. [1][3]

```python
from pocket_tts import TTSModel, export_model_state, import_model_state

model = TTSModel.load_model()

# One-time: ingest a user clip
voice_state = model.get_state_for_audio_prompt("alice_sample.wav")
export_model_state(voice_state, "alice.safetensors")

# Hot path: load cached voice profile
voice_state = import_model_state("alice.safetensors")
audio = model.generate_audio(voice_state, "Hello world.")
```

Eight built-in voices ship with the package: alba, marius, javert, jean, fantine,
cosette, eponine, azelma (Les Misérables names — likely studio-recorded "donor"
voices). [3]

### 3. License

- **Code (Python package)**: MIT. [1]
- **Model weights** (`kyutai/pocket-tts` on HF): **CC-BY-4.0**. [3]
- A separate weights variant `kyutai/pocket-tts-without-voice-cloning` exists, presumably
  for users who want to ship a product without exposing the cloning capability. [11]

CC-BY-4.0 permits commercial use with attribution. Combined with MIT code, this is
about as permissive as open TTS gets right now. The model card explicitly prohibits
impersonation without consent, fraud, and disinformation — but those are usage policies,
not license restrictions. [3]

### 4. Hardware requirements

This is the model's headline feature: **CPU-only is the recommended path**. [1][2]

- **Model size**: ~100M parameters. [3]
- **Tested perf**: ~6x real-time on a MacBook Air M4 using **only 2 CPU cores**. [1][3]
- **GPU**: explicitly **not recommended**. The Kyutai team tried it and "did not observe
  a speedup compared to CPU execution, notably because they use a batch size of 1 and a
  very small model". [12]
- **Memory footprint**: not stated explicitly, but a 100M-param FP32 model is roughly
  ~400MB on disk plus PyTorch overhead — comfortable on any modern Windows machine. The
  package also offers an optional `quantize` extra for INT8, though the README lists
  in-situ INT8 computation as an unsupported feature for now. [5][1]
- **PyTorch**: 2.5+, CPU build is fine. [3]

For a Windows tray app this is excellent — no CUDA dependency, no big VRAM bill.

### 5. Streaming support

Streaming is a first-class feature, not a bolt-on. [1][2]

- Generation runs at **12.5 Hz frame rate, 80ms per frame**; the model yields audio
  chunks as it generates, so playback can start before the full utterance is done. [13]
- The Python API exposes `generate_audio_stream()` alongside the batch
  `generate_audio()`. [13]
- A FastAPI server is included (`pocket-tts serve` → `http://localhost:8000`) that
  keeps the model resident in memory between requests. Exact endpoint paths weren't
  documented in the AGENTS.md I could pull, so plan to read `pocket_tts/server.py`
  before wiring the tray app to it. [1][13]

### 6. Latency

- **First-chunk latency**: ~**200ms** documented by Kyutai. [1][3]
- **Real-time factor**: ~6x faster than real-time on MacBook Air M4 with 2 cores —
  meaning a 10-word utterance (~3 seconds of audio) generates in ~500ms wall-clock
  and a 200-word utterance (~60 seconds of audio) generates in ~10 seconds, with the
  first audio coming out of the speaker after ~200ms. [1][3]
- Windows perf will depend on the CPU but should be in the same ballpark on any
  recent x86 chip with PyTorch 2.5+ CPU.
- The README also notes audio prompt encoding (Mimi) is "relatively slow" — that's why
  you cache to `.safetensors` after the first ingest. [1]

### 7. Quality

Kyutai claim "high-quality" but the public benchmarks comparing Pocket TTS head-to-head
with XTTS-v2 / F5-TTS / Chatterbox are not yet widely available. The `pocket-tts-technical-report`
URL exists but rendered empty when fetched, so I can't quote numbers from it directly. [14]

What's known:
- The 5-second cloning sample is competitive with what Chatterbox and XTTS-v2 advertise
  (Chatterbox: ~5s; XTTS-v2: ~6s). [6]
- **Major weakness for this user's app**: **English-only at launch**, with "more
  languages planned". [3] If the user needs German, French, or anything else, this is a
  blocker until Kyutai ships multilingual weights — Kyutai TTS 1.6B is `en_fr` but is
  GPU-class. Chatterbox and XTTS-v2 cover 20+ languages today. [6]
- The README warns that source clip artefacts (noise, reverb) get reproduced — same
  caveat as every other zero-shot cloner.
- Quality at 100M params will not match a 1.6B-class model on hard prosody or very
  expressive reads — that's the size tradeoff. For tray-app TTS of normal text, it's
  almost certainly fine.

### 8. Windows support

This is the one place Kyutai's docs are quiet. The evidence is indirect but positive:

- The PyPI wheel is **`pocket_tts-2.0.0-py3-none-any.whl`** — pure Python, OS-agnostic.
  Dependencies are PyTorch 2.5+ (well-supported on Windows) and standard Python audio
  libs. [5]
- No CUDA dependency means no Windows-specific CUDA toolkit hassle.
- Python 3.10–3.14 supported. [3][5]
- Kyutai's own perf numbers are quoted on macOS; their docs don't explicitly list
  Windows as tested. [1][3]
- A community Wyoming/Home Assistant integration runs Pocket TTS in production, which
  implicitly means it's been deployed on non-Mac platforms. [15]
- For belt-and-braces, the **sherpa-onnx** ONNX port of Kyutai TTS explicitly lists
  Windows + macOS + Linux + embedded. So if the official Python package hits a snag on
  Windows, the ONNX path is a documented fallback. [4]

Practical advice: try `pip install pocket-tts` in a fresh Windows venv first. If it
works (very likely), use the official package. If it doesn't, fall back to sherpa-onnx.

### 9. API surface

Three layers, all official: [1][3][13]

- **Python library**: `from pocket_tts import TTSModel`. Methods: `load_model()`,
  `get_state_for_audio_prompt(path_or_name)`, `generate_audio(state, text)`,
  `generate_audio_stream(state, text)`, plus `export_model_state` / `import_model_state`
  helpers.
- **CLI**:
  - `pocket-tts generate --voice <name_or_wav> --text "..."`
  - `pocket-tts export-voice <input.wav> <output.safetensors>`
  - `pocket-tts serve` — local FastAPI server with web UI on `:8000`
- **HTTP server**: built in (FastAPI), model stays resident. Exact endpoints not in the
  README excerpt I retrieved — read the source before wiring the tray app.

For a tray app, the natural shape is: spawn `pocket-tts serve` as a subprocess (or
import `TTSModel` directly into the tray process), POST text, stream audio chunks back
to a local audio output.

### 10. Official vs forks

The "official" version is the Kyutai-published `kyutai-labs/pocket-tts` repo and the
`pocket-tts` PyPI package. The README highlights several **community** ports the user
might run into and mistake for "the project": [1]

- PocketTTS.cpp (C++)
- TorchSharp (C#)
- Rust/Candle, Rust/XN
- WebAssembly
- sherpa-onnx (ONNX, multi-platform, multi-language bindings)

Use the Python one unless there's a specific reason to go ONNX (e.g., shipping without
PyTorch, Windows-on-ARM, etc.).

### Alternatives — one-paragraph comparison

If Pocket TTS doesn't fit (most likely reason: English-only), the leading 2026
alternatives for sample-based voice cloning are: **Chatterbox** (MIT, 23+ languages,
~5s reference, sub-200ms latency, beat ElevenLabs in blind tests, but needs a GPU);
**Coqui XTTS-v2** (20+ languages, mature, ~6s reference, but its license **restricts
commercial use** — fine for a personal tray app, not for selling); **F5-TTS** (very
expressive, only ~3GB VRAM, GPU-friendly edge deploys); **OpenVoice v2** (decent
clone-from-clip, multilingual); **GPT-SoVITS** (powerful but more setup). For a CPU-only
Windows tray app with no GPU available, Pocket TTS is in a near-unique niche — most
alternatives assume a GPU. If a GPU is available and English isn't enough, Chatterbox is
the strongest open-license option. [6]

## Sources

1. [kyutai-labs/pocket-tts (GitHub)](https://github.com/kyutai-labs/pocket-tts) — official repo, README, install/CLI/API examples, license. Active 2026.
2. [Pocket TTS: a high-quality TTS with voice cloning that runs on CPU (Kyutai blog, 2026-01-13)](https://kyutai.org/blog/2026-01-13-pocket-tts) — official launch announcement.
3. [kyutai/pocket-tts (HuggingFace model card)](https://huggingface.co/kyutai/pocket-tts) — license (CC-BY-4.0), model size (100M), languages (English only), built-in voices, code examples.
4. [Kyutai TTS landing page](https://kyutai.org/tts) — distinguishes Pocket TTS from Kyutai TTS 1.6B, mentions implementations (PyTorch / Rust / MLX / sherpa-onnx).
5. [pocket-tts on PyPI](https://pypi.org/project/pocket-tts/) — version 2.0.0 (2026-04-21), pure-Python wheel, supported Python versions.
6. [12 Best Open-Source TTS Models Compared (2025) — Inferless](https://www.inferless.com/learn/comparing-different-text-to-speech---tts--models-part-2) and [Best ElevenLabs Alternatives 2026 — ocdevel](https://ocdevel.com/blog/20250720-tts) — comparison data for Chatterbox / XTTS-v2 / F5-TTS / Kokoro.
7. [kyutai-labs/delayed-streams-modeling (GitHub)](https://github.com/kyutai-labs/delayed-streams-modeling) — home of Kyutai TTS 1.6B and STT models, separate from Pocket TTS.
8. [kyutai/tts-1.6b-en_fr (HuggingFace)](https://huggingface.co/kyutai/tts-1.6b-en_fr) — the larger English+French model for context.
9. [kyutai on X — "5 seconds of audio" voice cloning claim](https://x.com/kyutai_labs/status/2011047340115968076) — first-party claim. Vendor source, treat as marketing-leaning but consistent with the README.
10. [Pocket TTS: High-Quality Voice Cloning (Bytefer, Medium)](https://medium.com/@bytefer/pocket-tts-high-quality-voice-cloning-thats-fast-lightweight-fully-open-source-bcd596f9db54) — third-party walkthrough quoting "20 seconds" as a comfortable cloning length. Single source.
11. [kyutai/pocket-tts-without-voice-cloning (HuggingFace)](https://huggingface.co/kyutai/pocket-tts-without-voice-cloning) — variant weights without cloning.
12. [pocket-tts Windows/CUDA discussion (search summary)](https://github.com/kyutai-labs/pocket-tts) — README explicitly states GPU gave no speedup; CPU is the supported path.
13. [pocket-tts AGENTS.md](https://github.com/kyutai-labs/pocket-tts/blob/main/AGENTS.md) — internal architecture notes: Mimi encoder, 12.5Hz frame rate, 80ms frames, `generate_audio_stream`, FastAPI server.
14. [Pocket TTS technical report (Kyutai)](https://kyutai.org/pocket-tts-technical-report) — referenced but content didn't render at fetch time; left for future deep-dive.
15. [Wyoming Pocket TTS — Home Assistant Community](https://community.home-assistant.io/t/wyoming-pocket-tts-fast-local-tts-with-voice-cloning/978549) — third-party deployment, evidence of non-Mac use.

## Open questions

- **Windows specifically**: no first-party "tested on Windows" statement. The package
  *should* install (pure-Python wheel, PyTorch CPU on Windows is solid), but the user
  should validate `pip install pocket-tts` + `pocket-tts generate ...` on a clean
  Windows 11 venv before committing the tray-app architecture.
- **Multilingual roadmap**: "more languages planned" but no public ETA. If the user
  needs non-English in the foreseeable term, plan for an alternative.
- **Quality vs Chatterbox / XTTS-v2 head-to-head**: no published MOS or blind-test
  comparisons for Pocket TTS at this size. The technical report URL exists but didn't
  render — worth a follow-up fetch in a different client.
- **Server endpoints**: the FastAPI server's exact routes / streaming protocol weren't
  in the README excerpt I retrieved — read `pocket_tts/server.py` (or the AGENTS.md in
  full) before designing the tray-app's IPC.
- **Voice profile portability**: are `.safetensors` voice profiles tied to a specific
  model version? If Kyutai ships pocket-tts 3.0 with a different Mimi codec, do existing
  cached voices still load? Not documented.
