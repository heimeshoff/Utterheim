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
- **Color and contrast principles** — high contrast ratios, Light theme default
  (`ui:ThemesDictionary Theme="Light"` in `App.xaml`). Per ADR 0019 the active
  theme is user-selectable via Settings → Appearance and persists in
  `settings.json`. Accent color from system. No bespoke palette beyond the
  brand brushes documented under §Brand palette.
- **Iconography** — Fluent System Icons via `ui:SymbolIcon`. No custom glyphs except
  the brand mark.

The `## Design Principles` section of WhisperHeim's design.md applies verbatim:
local-first confidence, hotkey-first UI-second, progressive disclosure, Windows 11 native
feel, accessible.

---

## Brand palette

Mockingbird inherits four brand brushes from WhisperHeim. They are declared once
in `App.xaml` `<Application.Resources>` (alongside the merged wpfui dictionaries,
not inside them — wpfui's chain stays untouched) as `SolidColorBrush` resources
with stable `x:Key` names. Brushes are theme-independent fixed hex values — the
Light/Dark theme switch does not touch them — so consumers reference them via
`{StaticResource BrandPrimaryBrush}` rather than `DynamicResource`.

| Brush key | Hex | Use it for |
|---|---|---|
| `BrandPrimaryBrush` | `#FF25abfe` | Primary brand colour (cyan-blue). The voice-arcs in the logo, primary brand accents, hyperlink-style emphasis on dark surfaces. |
| `BrandAccentBrush` | `#FFff8b00` | Accent colour (orange). The head silhouette in the logo. Reserved for the brand mark; avoid as a UI fill. |
| `BrandDeepBrush` | `#FF005FAA` | Deep blue. Section-header glyphs in `ui:SymbolIcon` `Foreground`, hyperlink accents on dark surfaces. |
| `BrandDeepMutedBrush` | `#66005FAA` | Supplementary numerals such as version tags and footnotes. **Decorative only** — the RGBA renders ≈ 2.2:1 contrast on a Light Mica backdrop and fails WCAG body-text contrast. Matches WhisperHeim verbatim and is accepted as-is per the styleguide gate's "inherit wholesale" rule. Never use for body copy or for any text the user has to read at length. |

A page references the palette as e.g.:

```xml
<ui:SymbolIcon Symbol="PaintBrush24"
               Foreground="{StaticResource BrandDeepBrush}" />
```

Theme brushes (`ApplicationBackgroundBrush`, `TextFillColorPrimaryBrush`,
`CardBackgroundFillColorDefaultBrush`, …) stay `DynamicResource` so the
in-app Light/Dark/System swap re-renders without an app restart.

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

### Appearance modes

The Settings page hosts a three-tile picker (Light / Dark / System) modelled
verbatim on WhisperHeim's General-page picker. Selection persists to
`settings.json` as `appearanceMode: "Light" | "Dark" | "System"` per
[ADR 0019](../.agenthoff/knowledge/decisions/0019-appearance-mode-in-settings-json.md);
default for installs missing the field is `Light` (in-memory only — the file
is not rewritten on read).

Live swap uses `Wpf.Ui.Appearance.ApplicationThemeManager.Apply(...)` /
`ApplySystemTheme()` per
[`knowledge/research/wpfui-live-theme-swap-2026-05-04.md`](../.agenthoff/knowledge/research/wpfui-live-theme-swap-2026-05-04.md);
no app restart is required. Startup (`EntryPoint`) calls the same helper once
before `MainWindow.Show()` so the persisted preference applies on first paint
without flicker.

The active tile is highlighted with a 10% blue tint (`#19005FAA` over
transparent); the others stay transparent. Mica backdrop is unchanged across
all three modes (matches WhisperHeim).

---

## Page chrome

Every top-level page in mockingbird wraps its content in a single shared shell
so margins, max width, and the Mica-backdrop reveal stay consistent across
Speak, Voices, Settings, and About:

```xml
<ScrollViewer Background="{DynamicResource ApplicationBackgroundBrush}"
              VerticalScrollBarVisibility="Auto"
              HorizontalScrollBarVisibility="Disabled">
    <StackPanel Margin="40,36,40,32"
                MaxWidth="900"
                HorizontalAlignment="Center">
        <!-- page-specific content -->
    </StackPanel>
</ScrollViewer>
```

- The `ScrollViewer.Background` is the theme application brush so Mica shows
  through behind transparent cards.
- `Margin="40,36,40,32"` matches WhisperHeim's outer breathing room.
- `MaxWidth="900"` keeps line lengths readable on wide displays;
  `HorizontalAlignment="Center"` centres the column when the window is wider.
- Pages do **not** set their own `Background` — the inherited theme brush
  is the source of truth.

Page titles follow one of two patterns:

- **Hero** (Speak, About) — logo + bold "Mockingbird" + version tag + tagline
  per the brand mark spec. Reusable extraction `BrandHeroControl` lives under
  Reusable component map below; placement is per main-030 / main-032.
- **Small heading** (Voices, Settings) — single `TextBlock`
  `FontWeight="Light" FontSize="28"
  Foreground="{DynamicResource TextFillColorPrimaryBrush}"`. The Light-theme
  primary brush renders as dark text on the Mica backdrop and stays readable
  without competing with card-level headings on the same page.

---

## Section header

Section headers separate logical groups within a single page (e.g. Audio /
App / Diagnostics / Appearance on Settings). The composition is:

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,20">
    <ui:SymbolIcon Symbol="..." FontSize="24"
                   Foreground="{StaticResource BrandDeepBrush}"
                   Margin="0,0,10,0" />
    <TextBlock Text="SECTION LABEL"
               FontSize="10" FontWeight="Bold"
               Foreground="{DynamicResource TextFillColorSecondaryBrush}"
               VerticalAlignment="Center" />
</StackPanel>
```

- 24-px Fluent System icon glyph in `BrandDeepBrush`.
- Uppercase 10-pt bold label in the secondary text brush (so the label reads
  as muted relative to card-level headings).
- 20-px bottom margin separates the header from the first card under it.

The 40-px gap before each section header (i.e. the bottom margin of the
preceding section's last card — see §Card spec) is what gives the page its
vertical rhythm.

---

## Card spec

A card is the unit of grouped content inside a section. The shell is:

```xml
<Border Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
        CornerRadius="12" Padding="24" Margin="0,0,0,12">
    <!-- one-row or stacked-content composition -->
</Border>
```

- `CornerRadius="12"` and `Padding="24"` are the WhisperHeim values; do not
  bespokify per page.
- **Spacing rule**:
  - Between cards in the **same section**: `Margin="0,0,0,12"`.
  - On the **last card of a section** (before the next section header, or at
    the bottom of the page): `Margin="0,0,0,40"`. The wider gap is the visual
    separator between sections.

**One-row card** (label + description on the left, control on the right —
e.g. ToggleSwitch / ComboBox / Button):

```xml
<Border ...>
    <DockPanel>
        <StackPanel DockPanel.Dock="Left" VerticalAlignment="Center" MaxWidth="400">
            <TextBlock Text="Label"
                       FontSize="15" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
            <TextBlock Text="Description (≤2 lines, wraps if needed)."
                       FontSize="13"
                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                       Margin="0,4,0,0"
                       TextWrapping="Wrap" />
        </StackPanel>
        <ControlOfChoice DockPanel.Dock="Right"
                         HorizontalAlignment="Right"
                         VerticalAlignment="Center" />
    </DockPanel>
</Border>
```

**Stacked-content card** (label + description on top, control(s) below — e.g.
the Data path card or the Appearance picker):

```xml
<Border ...>
    <StackPanel>
        <TextBlock Text="Label"
                   FontSize="15" FontWeight="SemiBold"
                   Foreground="{DynamicResource TextFillColorPrimaryBrush}" />
        <TextBlock Text="Description."
                   FontSize="13"
                   Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                   Margin="0,4,0,16"
                   TextWrapping="Wrap" />
        <!-- control(s) -->
    </StackPanel>
</Border>
```

**Do not use `ui:CardControl`.** It is wpfui's older card chrome and produces
a visibly different surface (different padding, different background fill,
no rounded-12 rhythm) that drifts away from WhisperHeim. The only remaining
usage in v1 is the About page's Engine status panel, which main-032 will
restructure; new code should always use the `Border` pattern above.

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
