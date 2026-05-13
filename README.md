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

## Voice cloning setup

The eight built-in voices work out of the box, but **voice cloning** needs the
gated `kyutai/pocket-tts` weights from Hugging Face. Without auth, pocket-tts
silently falls back to the `kyutai/pocket-tts-without-voice-cloning` weights and
any clone attempt fails with a misleading "couldn't read the recording" toast.

1. **Accept the gated terms.** Sign in at https://huggingface.co and visit
   https://huggingface.co/kyutai/pocket-tts — click *Agree and access
   repository*. This is one-time, per HF account.
2. **Create a token.** Go to https://huggingface.co/settings/tokens → *New
   token*. A **Read** token (or fine-grained *Read access to public gated repos
   you've been granted access to*) is enough. Copy the `hf_…` string — Hugging
   Face shows it only once.
3. **Set `HF_TOKEN` as a Windows user environment variable.**
   - Press <kbd>Win</kbd> and type **Edit environment variables for your
     account** → open it.
   - Under *User variables for &lt;you&gt;*, click **New…**.
   - Variable name: `HF_TOKEN`. Variable value: paste the `hf_…` token.
   - Click **OK** on every dialog to save.
4. **Restart Utterheim** (fully exit from the tray, then relaunch). The
   embedded Python picks up `HF_TOKEN` at process start and downloads the
   voice-cloning weights on the next model load.

If you already have `huggingface-cli login` configured on this machine, that
works too — the embedded Python reads the same
`%USERPROFILE%\.cache\huggingface\token` file.

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
