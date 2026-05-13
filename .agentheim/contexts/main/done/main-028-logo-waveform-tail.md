---
id: main-028
title: Logo redesign — voice human-head mark
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-05
commit: 13a82e8
depends_on: [main-010]
blocks: [main-030, main-032]
tags: [branding, design, logo]
---

## Why

The current logo is the `main-010` placeholder: a generic speaking-person silhouette
(head + shoulders + concentric arcs). It was approved as a stand-in, not as the final
brand mark. The user wants the real thing: a logo that says "mockingbird" *and* "TTS"
at a glance, and that visually pairs with WhisperHeim (the dictation sibling) without
being identical.

## What

Replace the placeholder logo with a new design. As-shipped final design (signed off
2026-05-05, draft 3 — see ## Sign-off log):

An opaquely **filled** orange right-facing **human head in profile**, with three blue
Wi-Fi-style concentric arcs ("C" shapes) emerging from the mouth and fanning outward —
each arc larger than the one in front of it. The arcs sit **behind** the head silhouette
so the head reads as the dominant shape against any backdrop.

- **Head fill**: `#FFff8b00` (WhisperHeim brand orange).
- **Arc strokes**: `#FF25abfe` (WhisperHeim brand cyan-blue).
- **Two-colour, not theme-adaptive** — both colours are hard-coded in the SVG; neither
  is `currentColor`. The mark renders identically against Mica / light / dark / taskbar.
- **Filled silhouette + stroked arcs** treatment (the earlier line-art direction was
  reconsidered during sign-off).
- **Works at small sizes** — the head silhouette stays readable at 16 px (favicon /
  taskbar) even when the arc detail blurs. The arcs may visually merge with the head
  at the smallest sizes; that's acceptable as long as the head profile reads.

### Worker draft + sign-off gate

This task is **design-bearing** and the **worker drafts the SVG**. The flow is:

1. Worker produces a first SVG draft per the spec above and re-runs
   `Tools/RasteriseLogo/` to generate every PNG + the `.ico`.
2. Worker shows the user the rasterised outputs at multiple sizes (16, 24, 48,
   128, 256 at minimum) **before** marking the task done. The user signs off (or
   asks for revisions — pose, bar heights, stroke weight, body proportions).
3. Worker may iterate up to 3 SVG drafts before bouncing back for orchestrator
   help if the user is unhappy with the design direction. After sign-off, the
   styleguide + README updates land and the task moves to done.

Then re-run `main-012`'s rasterisation tool to regenerate every PNG and the
multi-resolution `.ico`. Update the brand-mark sections in `docs/styleguide.md` and
`contexts/main/README.md` to reflect that the logo is now signed-off-final, no longer a
placeholder.

## Acceptance criteria

- [x] `assets/branding/mockingbird-logo.svg` is replaced with the final voice
      human-head mark — filled orange (`#FFff8b00`) right-facing head profile +
      three blue (`#FF25abfe`) Wi-Fi-style concentric arcs fanning from the mouth,
      arcs behind the head.
- [x] User has signed off on the rasterised outputs at 16 / 24 / 48 / 128 / 256 px
      before the task moves to done. (Sign-off captured 2026-05-05 on draft 3 — see
      ## Sign-off log.)
- [x] `assets/branding/mockingbird-logo-{16,24,32,48,64,128,256,512}.png` are
      regenerated from the new SVG — head silhouette stays recognisable at
      16 / 24 px even if the arcs merge into the head at that scale.
- [x] `assets/branding/mockingbird.ico` (multi-res, layered for tray + taskbar)
      is regenerated and shows the new mark in Explorer / taskbar / tray.
- [x] The existing About page hero (`mockingbird-logo-256.png`, see
      `AboutPage.xaml` line 740 in main README) and any other in-app
      reference to the brand asset (nav header brand mark, BootstrapDialog,
      tray icon) pick up the regenerated rasters with no asset caching from
      the old design. (Verified by grep: every reference uses stable filenames
      — `mockingbird-logo-256.png`, `mockingbird.ico` — so re-rasterising lands
      automatically.)
- [x] `docs/styleguide.md` "Brand mark (logo)" section is updated: status flips
      from PLACEHOLDER to **signed off / final**, design description swapped to
      filled head + concentric arcs, two-colour treatment described, palette
      references added.
- [x] `contexts/main/README.md` brand asset entries flip from "placeholder, signed
      off" to **final**, with the as-shipped design called out.

## Notes

- Reference WhisperHeim's microphone hero composition for proportions and badge
  framing: `Border 88x88 CornerRadius=18 BorderThickness=3 BorderBrush=#FF25abfe
  Background=#1025abfe`, `Viewbox` inside with the icon at 24-unit canvas. The
  badge convention should carry over to mockingbird's hero — same shape, new mark.
  Note: inside that badge the background is a ~6%-alpha cyan-blue tint, and the
  arcs are full-opacity cyan-blue. The arcs should still read as
  distinct against the badge tint at 88 px; if they wash out during drafting,
  bump stroke weight before changing the colour.
- The rasterisation tool already exists at `Tools/RasteriseLogo/` (per main-012).
  No code changes there expected — just a fresh SVG input and a re-run.
- The mark is **not theme-adaptive** by design (per refinement). Both colours are
  hard-coded in the SVG, not `currentColor`. This trades theme-flexibility for
  brand-consistency — same mark, every backdrop.
- ~~Refinement decisions (locked 2026-05-04):~~ **Superseded by sign-off** — the
  original locked direction below described a perched mockingbird with three
  horizontal waveform-tail bars in line-art treatment. During drafting the user
  pivoted away from the bird metaphor entirely toward a voice-emitting human head
  with Wi-Fi-style arcs. The pre-pivot decisions are preserved here for history:
  - ~~Pose: perched in profile (single direction; suggested left-facing so tail
    trails right).~~
  - ~~Tail: three horizontal bars trailing behind the bird, varying heights, evenly
    spaced.~~
  - ~~Bird body: stroke `#FFff8b00` (orange).~~
  - ~~Waveform tail bars: stroke `#FF25abfe` (cyan-blue).~~
  - ~~Style: line-art stroke (similar weight to WhisperHeim `Microphone24`); not
    filled silhouette; not theme-adaptive.~~
  - ~~Workflow: worker drafts the SVG, user signs off on rasters before done.~~

  Carried forward from the original lock and still applicable to the as-shipped
  design: the two-colour palette (`#FFff8b00` orange + `#FF25abfe` blue), the
  not-theme-adaptive constraint, and the worker-drafts-then-user-signs-off
  workflow. What changed: subject (bird → human head), composition (perched bird
  + horizontal trailing bars → head profile + concentric Wi-Fi arcs from the
  mouth), and treatment (line-art → filled silhouette + stroked arcs).
- Pairs with main-029 (theme + colour adoption) and main-030 / main-032
  (page redesigns) — those tasks will look correct only when the new mark is in
  place. main-028 unblocks main-030 and main-032 (back-fill of `blocks:`).

## Sign-off log

- **2026-05-05** — Draft 1 (perched mockingbird, straight bars): user asked for curves instead of straight lines.
- **2026-05-05** — Draft 2 (perched mockingbird, sinusoidal curves): user pivoted the direction entirely — wanted human head profile + Wi-Fi-style concentric arcs.
- **2026-05-05** — Draft 3 (orange human head profile facing right + three concentric blue Wi-Fi arcs from the mouth): **approved**. Final.

## Outcome

Final brand mark shipped: filled orange (`#FFff8b00`) right-facing human-head
silhouette in profile + three blue (`#FF25abfe`) Wi-Fi-style concentric arcs
fanning from the mouth (arcs sit behind the head). Two-colour, not theme-adaptive.

Approved by Marco Heimeshoff on 2026-05-05 after a three-draft pivot from the
originally-locked perched-mockingbird direction (see ## Sign-off log). Pivot
documented under ## Notes so the history reads cleanly without confusing the
as-shipped description.

Files of record:

- `assets/branding/mockingbird-logo.svg` — source artwork (final).
- `assets/branding/mockingbird-logo-{16,24,32,48,64,128,256,512}.png` — rasters.
- `assets/branding/mockingbird.ico` — multi-resolution tray / taskbar icon.
- `docs/styleguide.md` — Brand mark (logo) section flipped to **signed off /
  final**, design description and palette refreshed.
- `.agenthoff/contexts/main/README.md` — brand-asset entries in the code-structure
  tree updated to "final".

In-app references (`src/Mockingbird/Views/Pages/AboutPage.xaml`,
`src/Mockingbird/Views/MainWindow.xaml`, `src/Mockingbird/Mockingbird.csproj`)
use stable filenames (`mockingbird-logo-256.png`, `mockingbird.ico`) — no
hash-busting, no XAML changes needed; the new bytes are picked up on the next
build.
