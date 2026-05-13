---
id: main-033
title: Design corrections — menu font/logo, voices order, settings layout, right-Ctrl hotkey, error strings
status: done
type: feature
context: main
created: 2026-05-05
completed: 2026-05-05
commit: 2b8fb3d
depends_on: [main-010]
blocks: []
tags: [ui, polish, styleguide, hotkey, follow-up-main-032]
---

## Why

A round of design and follow-up corrections after the main-032 About/Settings reshuffle. Several surfaces still don't match the WhisperHeim aesthetic the styleguide (`main-010`) calls for, two settings cards have the wrong layout pattern, the stop hotkey is on the wrong Control key for Marco's hand position, and two error messages still point users at the (now-pure-identity) About page instead of the Settings page where the Restart Engine button actually lives.

This is one bundled task because the items are small, share the same review pass, and most of them are a single-file XAML or string edit. The hotkey switch is the only behavioural change in the bundle.

## What

Six corrections, grouped:

### Branding / chrome
1. **Sidebar / menu font** — currently doesn't match the WhisperHeim font used in its NavigationView. Adjust the `MainWindow.xaml` `ui:NavigationView` (and the "Mockingbird" wordmark next to the logo) to use the same font WhisperHeim's nav uses. Compare against WhisperHeim's `MainWindow.xaml` and copy the FontFamily / FontWeight / FontSize choices verbatim.
2. **Sidebar logo** — the image rendered next to the "Mockingbird" wordmark in the navigation header is the old placeholder portrait ("the old black person"). Replace it with the same logo used at the top of the Speak page (the `BrandHeroControl` mark). WhisperHeim does this: same brand mark in the menu header *and* at the top of the dictation page. Mirror that pattern exactly.

### Voices page
3. **Section order** — switch the order of the two voice sections so **Cloned voices** appear *above* **Built-in voices**. (Currently built-ins are first.) Cloned voices are the user's own work and the differentiator; they should lead.

### Settings page
4. **Combobox layout for "Default voice" and "Output device" cards** — both currently render the combobox to the *right* of the label/description. Change both to render the combobox *below* the text (full-width, stacked), matching the styleguide's stacked-control pattern and what WhisperHeim does. Apply this layout to both cards.
5. **Settings card order** — re-order the cards on the Settings page to:
   1. Default voice
   2. Output device
   3. Data path
   4. Appearance
   5. HTTP port
   6. Stop key
   7. Engine status
   (i.e. data-path moves up to slot 3, appearance to slot 4, then the technical/diagnostic cards at the bottom.)

### Stop hotkey
6. **Switch double-tap from Left Ctrl to Right Ctrl** — both functionally and in every user-facing description.
   - Functional: `DoubleTapDetector` / `NativeMethods` watch right-Control (`VK_RCONTROL`) instead of left-Control (`VK_LCONTROL`).
   - Description: `appsettings.json` default, `SettingsPageViewModel` Stop-key card text, any tooltip / help string, the README's mention if any. All references should read "double-tap **Right Ctrl**" (or equivalent natural phrasing) instead of Left Ctrl.

### Error messages (follow-up to main-032)
7. **Replace "About page" pointers with "Settings page"** in error strings, since the Restart Engine button moved to Settings in main-032:
   - `Views/Pages/VoicesPage.xaml:54` — "Voice engine failed to start. **See the About page for details and retry.**"
   - `ViewModels/Pages/VoiceCloningViewModel.cs:456` — "Voice profile encoding failed. See the engine status footer or **About page**."
   - Audit for any other "About page" mentions in error or status strings and update them too. (Mentions in code comments / docstrings / `<see cref>` are fine to leave.)

## Acceptance criteria

- [ ] Menu / sidebar uses the same font (family, weight, size) as WhisperHeim's NavigationView; the "Mockingbird" wordmark text matches WhisperHeim's wordmark style.
- [ ] The image next to the "Mockingbird" wordmark in the sidebar is the same brand mark used at the top of the Speak page — no remnants of the old portrait.
- [ ] On the Voices page, the **Cloned voices** section renders above the **Built-in voices** section.
- [ ] On the Settings page, the **Default voice** and **Output device** cards each render their combobox stacked *below* the label/description, full-width, not beside it.
- [ ] On the Settings page, the cards appear in this top-to-bottom order: Default voice → Output device → Data path → Appearance → HTTP port → Stop key → Engine status.
- [ ] Double-tapping **Right Ctrl** triggers the stop signal; double-tapping Left Ctrl does *not*.
- [ ] All user-visible text describing the stop hotkey says "Right Ctrl" (or equivalent), with no remaining "Left Ctrl" wording in UI strings or `appsettings.json` defaults.
- [ ] `VoicesPage.xaml:54` error string and `VoiceCloningViewModel.cs:456` status string both reference the Settings page (where Restart Engine lives) instead of the About page.
- [ ] No other user-facing string still tells users to go to the About page for engine diagnostics / restart / retry.
- [ ] Manual smoke: launch the app, navigate Speak → Voices → Settings → About, confirm fonts and brand mark look like WhisperHeim, settings card order is correct, comboboxes are stacked, and a double-tap of Right Ctrl stops a playing utterance while Left Ctrl does not.

## Notes

- **Styleguide gate:** `depends_on: [main-010]` — gate is OPEN per BC README (signed off 2026-05-01). Frontend tasks may execute.
- **Reference implementations to copy from:** WhisperHeim's `MainWindow.xaml` (font + sidebar wordmark + brand mark placement) and its dictation page hero (parallel to our `BrandHeroControl`).
- **Files likely touched:**
  - `Views/MainWindow.xaml` — sidebar font + wordmark + logo image
  - `Views/Pages/VoicesPage.xaml` — section order + error string
  - `Views/Pages/SettingsPage.xaml` — card order + combobox layout for Default voice / Output device cards
  - `Services/Hotkey/DoubleTapDetector.cs`, `Services/Hotkey/NativeMethods.cs` — switch from `VK_LCONTROL` to `VK_RCONTROL`
  - `ViewModels/Pages/SettingsPageViewModel.cs` — Stop-key description text
  - `appsettings.json` — default hotkey value/description
  - `ViewModels/Pages/VoiceCloningViewModel.cs:456` — status string
  - `EntryPoint.cs` — only if it carries an LCtrl reference that's actually user-visible (else leave)
- **Out of scope:** any redesign of the cards themselves beyond layout and order; any change to *what* the engine-status card shows; any change to the About page (it's intentionally identity-only post main-032).
- **Why one task, not seven:** all are small XAML / string edits or single-file behavioural tweaks, all naturally reviewed together as "the post-main-032 design follow-up." If during execution the worker finds the hotkey switch wants its own commit, splitting the commit (not the task) is fine.
- **Possible regression risk on the hotkey switch:** confirm no other code path / docs / sample hook script (`examples/`) hard-codes Left Ctrl. The Claude Code Stop hook example added in commit 86e05d0 is unrelated (it speaks the assistant message, doesn't bind to the hotkey) but worth a quick grep.

## Outcome

All seven corrections landed; build is clean (`dotnet build` 0 warnings / 0
errors). Manual smoke not run by the worker — the user accepts "code in
place, not re-tested" per the assume-pass memory note.

### Branding / chrome
- `Views/MainWindow.xaml` — sidebar header now renders the Speak page brand
  mark (`mockingbird-logo-256.png` via `Image`, the same source
  `BrandHeroControl` uses) beside a 16pt Bold "Mockingbird" wordmark
  matching WhisperHeim's `BrandingTitle`. The inline-drawn portrait
  (`Ellipse` + `Path` graphics) is gone. A new `NavItemStyle` resource
  applies WhisperHeim's nav typography (`FontSize=11`, `FontWeight=Medium`)
  to all four `ui:NavigationViewItem`s (SPEAK / VOICES / SETTINGS / ABOUT,
  uppercased).

### Voices page
- `Views/Pages/VoicesPage.xaml` — Cloned voices section now renders above
  Built-in voices. Section comments updated to call out the swap. The
  `IsFailed` banner string updated from "See the About page for details and
  retry" to "See the Settings page …".

### Settings page
- `Views/Pages/SettingsPage.xaml` — full rewrite of the page body:
  - **Default voice** + **Output device** cards converted from
    `DockPanel` (combobox right) to stacked `StackPanel` (label + description
    on top, full-width `ComboBox` below) per the styleguide §Card spec
    "stacked-content card" pattern.
  - Card order now top-to-bottom: Default voice → Output device → Data path
    → Appearance → Start minimised → Launch at startup → HTTP port → Stop
    hotkey → Engine status → View logs. (Data path moved out of the
    Diagnostics section into a header-less slot between Audio and
    Appearance; Start minimised + Launch at startup were not in the task's
    explicit 7-item list, so they keep their relative position inside the
    APP section, which now sits between Appearance and Diagnostics.)

### Stop hotkey (Left Ctrl → Right Ctrl)
- `Services/Hotkey/DoubleTapDetector.cs` — default constructor virtual key
  switched from `VK_LCONTROL` to `VK_RCONTROL`; XML doc + file header
  comment updated.
- `EntryPoint.cs` — DI registration passes `VK_RCONTROL`; the
  `DoubleTapped` log message now says "(double-tap RCtrl)".
- `ViewModels/Pages/SettingsPageViewModel.cs` — both `StopHotkeyLabel`
  occurrences (constructor + backing-field initialiser) now read
  "Double-tap Right Ctrl".
- `appsettings.json` — `"DoubleTapKey": "RCtrl"` (field is currently
  descriptive only; not parsed back into the detector).
- `README.md` (repo-root) — "double-tap LCtrl" → "double-tap Right Ctrl".

### Error strings (follow-up to main-032)
- `Views/Pages/VoicesPage.xaml:55` — "About page" → "Settings page".
- `ViewModels/Pages/VoiceCloningViewModel.cs:456` — "About page" →
  "Settings page".
- Audited the codebase for other user-facing "About page" strings; the
  remaining occurrences are in code comments / `<see cref>` / legitimate
  About-page authorship lines and are intentionally left alone.

### Decisions recorded
- `.agenthoff/knowledge/decisions/0022-stop-hotkey-double-tap-right-ctrl.md`
  — explains why the hotkey switched from Left Ctrl to Right Ctrl.

### Domain memory updates (BC README)
- Updated the **Stop signal** glossary entry, the `DoubleTapDetector.cs`
  source-tree comment, the Voices-page Preview-via-queue narrative, the
  Settings-page intro / Audio section / Stop-hotkey card description, and
  the UI-shell narrative to reflect the new sidebar branding, the new
  card order, and the Right-Ctrl gesture.

### Files changed
See FILE_LIST in the worker return summary.

### Files NOT changed (intentionally)
- `dist/tray/appsettings.json` — build artifact (untracked per `git
  status`); regenerated on the next publish.
- `examples/perf/long-input.txt`, the various `done/` task files, vision /
  ADRs / research — historical records of when LCtrl was the gesture; not
  user-facing strings, not domain memory in the README sense.
- AboutPage code-behind / view-model / converters that mention "About
  page" in comments or `<see cref>` — those are about the page itself, not
  pointing users elsewhere.
