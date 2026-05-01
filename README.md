# Mockingbird

Local-first Windows TTS tray app that gives Claude Code a voice. Sister project
to [WhisperHeim](../../tooling/WhisperHeim/). See `.agenthoff/vision.md` for the
project's purpose and `.agenthoff/contexts/main/README.md` for the bounded
context.

## Status

**Walking skeleton (main-009).** The full architecture (HTTP server, queue,
NAudio playback, hotkey, tray, Serilog, path layout, CLI wrapper) is wired
end-to-end, but the synthesis engine is **stubbed** — it plays a 440 Hz test
tone instead of real speech. Replacing the stub with the real pocket-tts
sidecar is tracked as **main-011**.

## Build

```powershell
dotnet build mockingbird.sln -c Debug -v minimal
```

To run the tray app:

```powershell
dotnet run --project src\Mockingbird\Mockingbird.csproj
```

To publish a single-file CLI:

```powershell
dotnet publish src\Mockingbird.Cli\Mockingbird.Cli.csproj -c Release -r win-x64
```

## Try it

With the tray app running:

```powershell
# Plays a 1-second 440 Hz test tone through the default output device.
curl -X POST http://127.0.0.1:7223/speak `
     -H "Content-Type: application/json" `
     -d '{"text":"Hello, this is mockingbird.","voice":"test-voice"}'

# Stop everything.
curl -X POST http://127.0.0.1:7223/stop

# What voices are available?
curl http://127.0.0.1:7223/voices

# Or via the CLI wrapper:
mockingbird-speak --voice test-voice "task done"
```

The global stop hotkey is **double-tap LCtrl** (within 400 ms).

## Layout

```
src\
  Mockingbird\          WPF tray app (the host)
    Services\
      Tts\              ITtsEngine + StubTtsEngine (real engine: main-011)
      Speak\            SpeakRequest, SpeakQueue (Channel<T>), AudioPlayer (NAudio)
      Http\             SpeakServer (Kestrel minimal API on 127.0.0.1:7223)
      Hotkey\           DoubleTapDetector (low-level keyboard hook)
      Settings\         DataPathService (path layout per ADR 0005)
    Views\              MainWindow + BootstrapDialog (Wpf.Ui Mica skeleton)
    EntryPoint.cs       Composition root
  Mockingbird.Cli\      mockingbird-speak — single-file CLI wrapper
```

Architecture decisions live in `.agenthoff/knowledge/decisions/0001..0008-*.md`.
