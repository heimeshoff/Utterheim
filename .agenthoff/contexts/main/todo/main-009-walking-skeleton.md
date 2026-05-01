---
id: main-009
title: Walking skeleton — Claude hook → HTTP → sidecar → audio out
status: todo
type: spike
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-001, main-002, main-003, main-004, main-005, main-006, main-007, main-008]
blocks: [main-010]
tags: [foundation, skeleton, prototype]
---

## Why

This is mockingbird's first prototype. Every architectural foundation decision (main-001 through main-008) needs proof that the chosen stack runs end-to-end before any feature work begins. The skeleton is **feature-thin, architecture-thick** — it does almost nothing functionally interesting, but it exercises every layer the foundation ADRs decided on. If the skeleton can't ship, one of the foundation decisions is wrong and we revisit before pouring code on top.

This is also the **moment code first appears in the project** — the brainstorm phase produced only markdown.

## What

Build the thinnest possible end-to-end path from a shell HTTP call to audio playing through the speakers. Specifically:

1. **Project skeleton** (per main-001): `mockingbird.csproj` with `net9.0-windows`, `WinExe`, `UseWPF=true`, `x64`, server GC, Nullable + ImplicitUsings on. Solution file `mockingbird.sln`. Wpf.Ui.Tray + Serilog + NAudio NuGet references.

2. **Path layout + bootstrap** (per main-005, main-008):
   - Create `%APPDATA%\Mockingbird\` and `%LOCALAPPDATA%\Mockingbird\` on first run.
   - Copy WhisperHeim's `DataPathService` and rename to mockingbird paths (per main-006).
   - Create `<dataPath>\voices\library.json` (empty list is fine for v1 skeleton).
   - Bring up Serilog with the rolling file sink and the sidecar-stdout redirect sink.

3. **Python sidecar bootstrap** (per main-002, main-008):
   - On first launch, run an install script that prepares the embeddable Python at `%LOCALAPPDATA%\Mockingbird\runtime\python\` and pip-installs `pocket-tts` and deps. Mirror WhisperHeim's `ModelDownloadDialog` UX for progress.
   - On every launch, spawn `runtime\python\python.exe -m pocket_tts.server --host 127.0.0.1 --port 0`. Read the assigned port from stdout/stderr or a known hand-off file. Bind loopback only.
   - Health-check the sidecar (e.g., `GET /healthz` if pocket-tts exposes it; otherwise a benign synthesis call) before declaring it ready.
   - Capture sidecar stdout/stderr into Serilog as `sidecar`-tagged events.
   - Tear it down cleanly when the tray app exits.

4. **`ITtsEngine` interface** (per main-002): one method roughly `IAsyncEnumerable<byte[]> StreamAsync(string text, string voiceId, CancellationToken ct)`. The pocket-tts implementation calls the sidecar's HTTP synthesis endpoint and yields audio chunks. The interface is the seam future engines plug into.

5. **HTTP speak endpoint** (per main-003): Kestrel-hosted minimal API on `127.0.0.1:7223` with:
   - `POST /speak` → enqueue, return `202 Accepted` with `{requestId, queuePosition}`.
   - `POST /stop` → trigger stop+drain (per main-004).
   - `GET /voices` → return the (initially: just the pocket-tts built-ins).
   - `GET /status` → playback state, queue length, sidecar health.

6. **Speak queue + playback worker** (per main-007):
   - `Channel<SpeakRequest>` with single-reader semantics.
   - Long-running `Task` consumes the channel, calls `ITtsEngine.StreamAsync`, pipes audio chunks to NAudio (`WasapiOut` or `WaveOutEvent` — pick the one matching pocket-tts's sample rate) on the user's default output device.
   - On request completion, mark done; advance to next.
   - On cancellation: stop NAudio, cancel sidecar HTTP call, drain channel (per main-004).

7. **Stop hotkey** (per main-006): copy `GlobalHotkeyService` + supporting files from WhisperHeim. Implement a thin double-tap detector for LCtrl (within ~400ms window). On detection, hit the same stop+drain path the HTTP `/stop` endpoint uses.

8. **Tray shell** (per main-001):
   - Tray icon with right-click menu: "Show window", "Stop speaking", "Exit".
   - A minimal main window using Wpf.Ui that just shows the sidebar shell + a placeholder "ready" content area. **No real UI yet** — that's main-010 and beyond.
   - The "speaking person" logo is a stub; final logo lands in main-010.

9. **CLI wrapper** (per main-003): a tiny `mockingbird-speak.exe` (single-file `dotnet publish`) that wraps `POST /speak`. Lets a Claude hook do `mockingbird-speak --voice alba "task done"` without remembering curl flags.

## Acceptance criteria

These are observable behaviours, not architectural promises. A user (or a smoke test) can verify each one.

- [ ] **App launches.** Run `mockingbird.exe`; tray icon appears within 5 seconds; main window opens on left-click; closes to tray on close-button.
- [ ] **First-run bootstrap completes.** On a clean machine with no prior install, the bootstrap dialog appears, completes the Python+pocket-tts install with progress, and ends with the tray ready. Subsequent launches skip the dialog.
- [ ] **Speak request plays audio.** With the app running, `curl -s -X POST http://127.0.0.1:7223/speak -H "Content-Type: application/json" -d '{"text":"Hello, this is mockingbird.","voice":"<built-in voice id>"}'` returns `202 Accepted` within ~100 ms, and audio plays through the default output device within ~2 seconds.
- [ ] **Streaming is real.** A long text (~200 words) starts playing audio before the full synthesis completes (first-chunk latency well under the total synthesis time).
- [ ] **Concurrent requests queue.** Fire two `POST /speak` calls back-to-back; both play, in order, with no overlap and no drop.
- [ ] **HTTP stop works.** While audio is playing, `curl -X POST http://127.0.0.1:7223/stop` halts playback within ~200 ms and clears any queued requests.
- [ ] **Double-tap LCtrl stops.** Same outcome as HTTP stop, triggered by the global hotkey.
- [ ] **Voice list works.** `GET /voices` returns the pocket-tts built-ins as `{id, name, engine: "pocket-tts", isBuiltIn: true}`.
- [ ] **CLI wrapper works.** `mockingbird-speak --voice <id> "test"` plays audio.
- [ ] **Logs are useful.** A failed sidecar start, a missing voice, or a port collision produces a clear log line and a tray toast (per main-008).
- [ ] **Clean shutdown.** Closing the tray icon's "Exit" terminates the sidecar, releases port 7223, and removes the tray icon. No zombie `python.exe` processes.

## Out of scope for this skeleton

Explicitly defer to subsequent feature tasks:
- Voice management UI (cloning, listing, deleting cloned voices) — depends on main-010 (styleguide).
- Microphone / system-audio sample capture UI — depends on main-010.
- Settings page (port override, drain-vs-current-only toggle, output device picker) — depends on main-010.
- Voice profile metadata editing — later.
- Auto-update, code signing, installer — v1.5 per main-008.
- Priority lanes / barge-to-front — v1.5 per main-007.

## Notes

- The eight foundation ADRs (0001–0008) MUST be committed before the skeleton work begins — that's why this task `depends_on` all of them. The decision tasks should commit ADR files and nothing else; the skeleton is where they all materialise as code.
- Validate `pip install pocket-tts` in a fresh Windows 11 venv as the very first step of skeleton work. If pocket-tts can't install on Windows, the foundation decision (main-002) needs revisiting *before* you keep building. Don't paper over a broken sidecar with mocks.
- A synthesis smoke test against a built-in voice (one of `alba`, `marius`, etc.) is the minimum proof that the engine works. The voice library + clone flow is main-011 territory, not skeleton.
- After this task finishes, mockingbird is ready for feature work. The user should hear "Hello, this is mockingbird." in a real voice as the moment of validation.
