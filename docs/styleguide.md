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
**voice-emitting human head** (output — speaking). The contrast is intentional and the
single most visible piece of brand differentiation: WhisperHeim listens, mockingbird
speaks.

- Asset: [`assets/branding/mockingbird-logo.svg`](../assets/branding/mockingbird-logo.svg)
- Design: an opaquely **filled** orange right-facing human-head silhouette in profile,
  with three blue Wi-Fi-style concentric arcs ("C" shapes) fanning outward from the
  mouth, each arc larger than the one in front of it. The arcs sit **behind** the head
  silhouette so the head reads as the dominant shape. Two-colour, **not theme-adaptive**:
  - Head fill — `#FFff8b00` (WhisperHeim brand orange).
  - Arc strokes — `#FF25abfe` (WhisperHeim brand cyan-blue).
  Both colours are hard-coded in the SVG; neither is `currentColor`. The mark renders
  identically against any backdrop (Mica, light, dark, taskbar). The earlier line-art
  treatment was reconsidered during sign-off — the final treatment is **filled silhouette
  + stroked arcs**, not line-art.
- **Small-size readability** — the head silhouette must stay recognisable at 16 px
  (favicon / taskbar) even when the arc detail blurs. At the smallest sizes the arcs
  may visually merge into the head; that is acceptable as long as the head profile
  reads.
- **Status: signed off / final** — approved by Marco Heimeshoff on 2026-05-05 (main-028,
  draft 3).

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
| Voice human-head logo (vector) | `assets/branding/mockingbird-logo.svg` | **Final** — signed off 2026-05-05 (main-028, draft 3). Filled orange (`#FFff8b00`) right-facing head profile + three blue (`#FF25abfe`) Wi-Fi-style concentric arcs from the mouth; not theme-adaptive. |
| Logo PNG sizes (16, 24, 32, 48, 64, 128, 256, 512) | `assets/branding/mockingbird-logo-{size}.png` | **Final** — regenerated from the signed-off SVG (originally produced by `main-012`'s rasteriser). Two-colour mark on transparent. |
| Logo `.ico` (multi-resolution, for tray + taskbar) | `assets/branding/mockingbird.ico` | **Final** — regenerated from the signed-off SVG. Layers: 16, 24, 32, 48, 64, 128, 256 (PNG-compressed). Wired as `<ApplicationIcon>` and bound to `tray:NotifyIcon` in `Views\MainWindow.xaml`. |

Notes:

- The mark is two-colour and **not theme-adaptive** by design: orange head + blue arcs
  render the same against Mica, light, dark, and the Windows 11 taskbar. The earlier
  `currentColor` line-art approach was retired when the final mark landed.
- The PNG and `.ico` assets are committed alongside the SVG. They are regenerated only
  when the source SVG changes, via the standalone helper at
  `Tools\RasteriseLogo\RasteriseLogo.csproj` (run with
  `dotnet run --project Tools\RasteriseLogo\RasteriseLogo.csproj`). The helper lives
  outside `mockingbird.sln` so day-to-day builds don't pull SkiaSharp into the main
  build graph.
- The WPF main window references the rasters by stable filename
  (`mockingbird-logo-256.png`, `mockingbird.ico`) — no hash-busting, so a new SVG +
  re-rasterise picks up automatically with no `.csproj` or XAML changes.
- The `.ico` is what Windows uses for the tray, taskbar, and Explorer .exe icon; the
  PNGs are used in-app (e.g. About page hero at 256 → 128 displayed).

---

## Sign-off

**signed-off-by: Marco Heimeshoff on 2026-05-01**

The placeholder speaking-person silhouette is approved as the working brand mark for v1.
The proposed tagline ("Local voices for Claude Code") is accepted. The page set, hotkey
gesture, tray menu, and reusable component map are accepted. Frontend feature tasks may
now be promoted from `backlog/` to `todo/`.
