# Utterheim

Local-first Windows TTS tray app that gives Claude Code a voice. Sister project
to [WhisperHeim](../../tooling/WhisperHeim/). See `.agentheim/vision.md` for the
project's purpose and `.agentheim/contexts/main/README.md` for the bounded
context.

## Status

**Walking skeleton (main-009).** The full architecture (HTTP server, queue,
NAudio playback, hotkey, tray, Serilog, path layout, CLI wrapper) is wired
end-to-end, but the synthesis engine is **stubbed** — it plays a 440 Hz test
tone instead of real speech. Replacing the stub with the real pocket-tts
sidecar is tracked as **main-011**.

## Build

```powershell
dotnet build utterheim.sln -c Debug -v minimal
```

To run the tray app:

```powershell
dotnet run --project src\Utterheim\Utterheim.csproj
```

To publish a single-file CLI:

```powershell
dotnet publish src\Utterheim.Cli\Utterheim.Cli.csproj -c Release -r win-x64
```

## Try it

With the tray app running:

```powershell
# Plays a 1-second 440 Hz test tone through the default output device.
curl -X POST http://127.0.0.1:7223/speak `
     -H "Content-Type: application/json" `
     -d '{"text":"Hello, this is utterheim.","voice":"test-voice"}'

# Stop everything.
curl -X POST http://127.0.0.1:7223/stop

# What voices are available?
curl http://127.0.0.1:7223/voices

# Or via the CLI wrapper:
utterheim-speak --voice test-voice "task done"
```

The global stop hotkey is **double-tap Right Ctrl** (within 400 ms).

## Claude Code plugin

To have Claude Code speak its end-of-turn summaries and attention prompts through
Utterheim, install the bundled `utterheim-narrator` plugin. From inside Claude
Code, in the project where you want the plugin:

```
/plugin marketplace add <path-to-utterheim-repo>/claude-code-plugin
/plugin install utterheim-narrator@utterheim-narrator
```

See [`claude-code-plugin/README.md`](claude-code-plugin/README.md) for per-repo
voice selection (`/narrator`), muting, and updates.

## Layout

```
src\
  Utterheim\          WPF tray app (the host)
    Services\
      Tts\              ITtsEngine + StubTtsEngine (real engine: main-011)
      Speak\            SpeakRequest, SpeakQueue (Channel<T>), AudioPlayer (NAudio)
      Http\             SpeakServer (Kestrel minimal API on 127.0.0.1:7223)
      Hotkey\           DoubleTapDetector (low-level keyboard hook)
      Settings\         DataPathService (path layout per ADR 0005)
    Views\              MainWindow + BootstrapDialog (Wpf.Ui Mica skeleton)
    EntryPoint.cs       Composition root
  Utterheim.Cli\      utterheim-speak — single-file CLI wrapper
```

Architecture decisions live in `.agentheim/knowledge/decisions/0001..0008-*.md`.
