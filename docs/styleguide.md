# Mockingbird Styleguide

This is the single source of truth for mockingbird's frontend look and feel. It exists to
satisfy the styleguide gate captured in `main-010` and referenced by every frontend feature
task in `.agenthoff/contexts/main/`.

> **Status:** Signed off by Marco Heimeshoff on 2026-05-01. Frontend feature tasks may
> now be promoted from `backlog/` to `todo/`. The placeholder logo was approved in place;
> rasterisation is tracked as `main-012`.

---

## Inherited from WhisperHeim

Mockingbird inherits its design language wholesale from WhisperHeim. The authoritative
reference is:

> [`..\..\tooling\WhisperHeim\design.md`](../../../tooling/WhisperHeim/design.md)

What we adopt **unchanged**:

- **Mica backdrop** — `WindowBackdropType="Mica"` on `ui:FluentWindow`, no custom chrome.
- **Fluent controls** — WPF UI (lepoco/wpfui) controls throughout: `ui:Button`,
  `ui:TitleBar`, `ui:SymbolIcon`, `ui:NavigationView` patterns. No bespoke control library.
- **Typography** — Segoe UI Variable for everything. Default WPF UI sizes; no custom
  font scale. Headings use `FontWeight="Light"`, sub-headings `SemiBold`, body default.
- **Sidebar nav layout** — single window, left vertical sidebar (~220 px wide) with
  icon + label rows, content area on the right. Matches WhisperHeim's shell.
- **Recording controls component spec** — from WhisperHeim design.md section 6 (TTS,
  Section B Voices). The shared recording controls (audio level meter, duration display
  with 5-second minimum + progress bar, start/stop buttons, voice name input, save
  button) are reused identically in mockingbird's voice-cloning flow.
- **Color and contrast principles** — high contrast ratios, dark theme default
  (`ui:ThemesDictionary Theme="Dark"` in `App.xaml`), accent color from system. No
  bespoke palette.
- **Iconography** — Fluent System Icons via `ui:SymbolIcon`. No custom glyphs except
  the brand mark.

The `## Design Principles` section of WhisperHeim's design.md applies verbatim:
local-first confidence, hotkey-first UI-second, progressive disclosure, Windows 11 native
feel, accessible.

---

## Mockingbird divergences

The places where mockingbird intentionally departs from WhisperHeim. Each divergence is
listed here so they don't drift into "every page is slightly different from WhisperHeim
for no reason".

### Brand mark (logo)

WhisperHeim uses a microphone silhouette (input — listening). Mockingbird uses a
**speaking-person silhouette** (output — speaking). The contrast is intentional and the
single most visible piece of brand differentiation: WhisperHeim listens, mockingbird
speaks.

- Placeholder asset: [`assets/branding/mockingbird-logo.svg`](../assets/branding/mockingbird-logo.svg)
- Placeholder design: monochrome silhouette of a head + shoulders facing right, with
  three concentric sound-wave arcs emanating from the mouth area. Drawn with
  `stroke="currentColor"` so it tints cleanly against any Mica backdrop and adapts to
  light/dark theme.
- **Status: PLACEHOLDER** — pending user-supplied artwork. The silhouette is sufficient
  for the WPF shell and About page during early development; it is **not** the final
  brand mark.

### Page set

WhisperHeim has seven pages: General, Dictation, Templates, Transcribe Files, Transcripts,
Text to Speech, About. Mockingbird does **not** ship any of the dictation- or
transcription-oriented pages — that's WhisperHeim's domain. Mockingbird's pages are:

| Page | Equivalent in WhisperHeim | Notes |
|---|---|---|
| **Speak** | TTS section A (Speak) | The primary page. Text input → voice selector → play / stop / save. |
| **Voices** | TTS section B (Voices) | Voice library + clone-new-voice flow (mic and system-audio sources). |
| **Settings** | General | "Start minimized" + "Launch at startup" + future preferences. |
| **About** | About | Logo, tagline, version, model status (pocket-tts engine status). |

Mockingbird does **not** have: Dictation, Templates, Transcribe Files, Transcripts.

### Tagline

> **Proposed tagline: "Local voices for Claude Code"**
>
> _(TBD — pending user sign-off below. Alternatives the user may prefer: "Local TTS for
> Claude Code", "Give Claude a voice, locally", "Voices for Claude, on your machine".)_

Replaces WhisperHeim's "Live dictation powered by Whisper". The chosen tagline appears
on the About page and as the window subtitle / `TitleBar` text where appropriate.

### Hotkeys

Mockingbird has exactly **one** global hotkey:

| Gesture | Action |
|---|---|
| **Double-tap LCtrl** | Stop current speech and (per ADR 0004) drain the speak queue. |

Notably mockingbird does **not** implement WhisperHeim's `Ctrl+Win+Ä` "read selected text
aloud" gesture. The reason: speak requests come in over HTTP from Claude Code, not from
the user selecting text on screen. There is no input gesture for "speak this" — only for
"stop speaking". Documented in ADR 0006 (LCtrl gesture) and the vision.

The Dictation-page-style "hotkey reference card" from WhisperHeim therefore collapses to
a single line on mockingbird's About or Settings page.

### Tray menu

Per main-009, the tray menu is fixed at three items:

1. **Show window** — restore / activate the main window.
2. **Stop speaking** — invokes `SpeakQueue.StopAndDrain()`. Same effect as the LCtrl
   double-tap.
3. **Exit** — clean shutdown via the host's exit action.

WhisperHeim's tray menu (Start/Stop Call Recording, Settings, Exit) doesn't apply because
mockingbird has no recording-of-meetings mode.

---

## Reusable component map

The components below come from WhisperHeim design.md section 6 and are used in mockingbird
unchanged. When implementing the corresponding mockingbird page, reuse the spec — do not
redesign.

| Mockingbird use | WhisperHeim spec source |
|---|---|
| Recording controls (level meter, duration display with 5-second minimum + progress bar, start/stop, voice name input, save button) | design.md §6 Section B "Shared controls" |
| Source toggle (Microphone vs System Audio) — segmented control / tab pair, distinct icons | design.md §6 Section B, "source toggle" |
| Speak section composition (text input → voice selector → play / stop / save buttons → status line) | design.md §6 Section A "What's on it" |
| Voices list with preview + delete affordances | design.md §6 Section B "Custom Voices list" |
| Microphone device selector + quality tip ("use a quiet environment") | design.md §6 Section B "Microphone mode" |
| Output device selector + tip ("close other audio apps...") | design.md §6 Section B "System Audio mode" |

Components that are **not** reused: anything tied to dictation overlays, template
placeholders, transcript export, speaker diarization, drag-and-drop file zones — these
belong to WhisperHeim pages mockingbird doesn't ship.

---

## Brand assets

| Asset | Path | Status |
|---|---|---|
| Speaking-person logo (vector) | `assets/branding/mockingbird-logo.svg` | **Approved placeholder** — signed off 2026-05-01 as the working brand mark for v1. |
| Logo PNG sizes (16, 24, 32, 48, 64, 128, 256, 512) | `assets/branding/mockingbird-logo-{size}.png` | **Generated** by `main-012` from the source SVG. White silhouette on transparent. |
| Logo `.ico` (multi-resolution, for tray + taskbar) | `assets/branding/mockingbird.ico` | **Generated** by `main-012`. Layers: 16, 24, 32, 48, 64, 128, 256 (PNG-compressed). Wired as `<ApplicationIcon>` and bound to `tray:NotifyIcon` in `Views\MainWindow.xaml`. |

Notes:

- The placeholder SVG is intentionally simple geometry (head circle + trapezoidal body +
  three arc waves) so it's recognisable as "person speaking" without pretending to be
  finished artwork.
- The PNG and `.ico` assets are committed alongside the SVG. They are regenerated only
  when the source SVG changes, via the standalone helper at
  `Tools\RasteriseLogo\RasteriseLogo.csproj` (run with
  `dotnet run --project Tools\RasteriseLogo\RasteriseLogo.csproj`). The helper lives
  outside `mockingbird.sln` so day-to-day builds don't pull SkiaSharp into the main
  build graph.
- The WPF main window also displays the SVG-derived geometry inline (in the sidebar and
  centre splash) — that's a parallel rendering path and stays unchanged. The `.ico` is
  what Windows uses for the tray, taskbar, and Explorer .exe icon.
- The silhouette is rasterised in white on a transparent background so it reads cleanly
  on a dark Windows 11 taskbar / Mica backdrop. The colour choice is mechanical, not
  branded; if a future task introduces a coloured brand mark the rasteriser
  (`Tools\RasteriseLogo\Program.cs`) is the single place to change.

---

## Sign-off

**signed-off-by: Marco Heimeshoff on 2026-05-01**

The placeholder speaking-person silhouette is approved as the working brand mark for v1.
The proposed tagline ("Local voices for Claude Code") is accepted. The page set, hotkey
gesture, tray menu, and reusable component map are accepted. Frontend feature tasks may
now be promoted from `backlog/` to `todo/`.
