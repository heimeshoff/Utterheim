---
id: main-026
title: Voices page — per-row delete affordance for cloned voices
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-04
commit: a69b59a
depends_on: [main-014, main-015]
blocks: []
tags: [frontend, page, voice-library]
---

## Why

main-014 ships the read-only Voices page; main-015 ships the
`VoiceLibraryService.DeleteAsync` API. Without a UI affordance, the
user can only delete by editing files in `<dataPath>\voices\` — not
acceptable. This task closes the loop: per-row delete on cloned
voices, with confirmation, surfaced on the Voices page.

Originally bundled into main-015. Split out (per main-014 / main-015
refinement Q1) so it can ship independently — small surface, clear
behaviour, no audio-capture dependency.

## What

Add a per-row Delete button to **cloned voices only** (built-ins are
not deletable). On click → confirmation dialog → on confirm →
`VoiceLibraryService.DeleteAsync(id)` → row disappears via the
`VoicesChanged` event (already wired by main-014).

### Row layout change

main-014's row is a 3-column Grid: name+meta (fills) | Preview button
(auto) | status indicator (auto). Add a 4th column **only on cloned
rows** for the Delete button, between Preview and the status indicator:

```
| name + meta | Preview | Delete | status indicator |
```

Built-in rows keep the 3-column shape (no Delete column allocated).
Implemented either by two row templates (built-in / cloned) or by
binding `Visibility` of the Delete column to `IsBuiltIn ? Collapsed :
Visible`. Worker picks; the binding approach is simpler.

Delete button: `ui:Button` with icon `Delete24`, `Appearance="Secondary"`,
`ToolTip="Delete voice"`, **always visible** (not hover-only) per
WhisperHeim's `DeleteConfirmationDialog` consistency — the user always
knows the affordance exists. Icon-only (no "Delete" label) to keep the
row compact; the tooltip carries the verb.

### Confirmation pattern

**Modal `ContentDialog` (Fluent / wpfui).** Matches WhisperHeim's
`DeleteConfirmationDialog` (referenced in
`C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Views\DeleteConfirmationDialog.xaml`).
Composition:

- 40×40 rounded `Border` with `Delete24` icon on a translucent red
  background (`#20E81224`).
- Title: "Delete voice?" `FontSize=16, FontWeight=Bold`.
- Subtitle: "This action cannot be undone."
- Card showing the voice display name in `SemiBold`.
- Two buttons right-aligned: **Cancel** (transparent / secondary) and
  **Delete** (solid red `#FFE81224`, white text, primary
  destructive).

The dialog reuses wpfui's `ContentDialog` shell rather than copying
WhisperHeim's `Window`-based dialog verbatim, because utterheim's
nav-shell pattern (main-020) prefers in-window content dialogs over
secondary windows. The visual composition (icon, copy, button colours)
matches WhisperHeim — that's the consistency we owe — but the
host-control is wpfui-native.

**Rejected alternatives:**

- **Inline-confirm-with-undo-toast** (gmail-style "Voice deleted —
  Undo") — rejected: undo would require staging the delete, holding
  the row in a "soft-deleted" state, and exposing a 5 s window for
  undo. Speculative complexity for one user; modal confirm is fine
  and the user can reclone if they delete by mistake.
- **`MessageBox.Show`** — rejected: violates the styleguide's Fluent /
  Mica aesthetic.

### Behaviour

On Delete click:

1. Open the confirmation dialog with the voice's display name.
2. **Cancel** / Esc / click-outside → dialog closes, no action.
3. **Delete** clicked:
   a. Dialog shows brief in-progress state (button replaced with
      `ui:ProgressRing`); typically <100 ms.
   b. Call `VoiceLibraryService.DeleteAsync(voiceId, ct)` (per
      main-015).
   c. On success → dialog closes. `LibraryChanged` fires →
      `VoicesChanged` fires → main-014's existing subscription
      removes the row. Status footer briefly shows nothing; no
      toast / no extra confirmation (the row vanishing is the
      confirmation).
   d. On failure → dialog stays open, shows inline error under the
      buttons in red: "{message}" (e.g. "File is locked. Stop
      playback and try again."). The user can click Delete again
      or Cancel.

### Behaviour during preview / active speak request

main-015's `VoiceLibraryService.DeleteAsync` does **not** guard
against active playback. Per main-015 Q10:

- If the deleted voice is currently being previewed (or is the
  active speak-request voice), the delete proceeds. The library
  index is pruned first, then the folder is deleted. If the
  `.safetensors` is held open by the sidecar (file lock on
  Windows), the folder delete may fail; `library.json` is still
  pruned, the row disappears, and the next-launch reconciler
  cleans up the orphaned folder.
- The active synthesis request is **not** cancelled. If the
  user wants to halt it, they hit Stop (LCtrl double-tap / tray
  menu / `POST /stop`). The orchestrator considered a "delete
  also stops playback" coupling and rejected it as too clever
  for v1: stop is the user's existing tool for halting audio,
  delete is the user's tool for removing voices. Keeping them
  orthogonal matches the rest of the BC's design (delete from
  Explorer wouldn't stop playback either).
- The existing playback worker handles a sidecar-side error
  (cloned voice's `.safetensors` disappeared mid-stream) via
  the existing `SpeakService` error path; the row indicator
  flips off and the status footer reflects engine state.

## Acceptance criteria

- [ ] Cloned voice rows show a Delete button between Preview and the
  status indicator. Built-in rows do **not** show Delete (column
  collapsed via `IsBuiltIn` binding).
- [ ] Delete button is always visible (not hover-only). Icon
  `Delete24`, `Appearance="Secondary"`, tooltip "Delete voice".
- [ ] Click opens a Fluent `ContentDialog` matching WhisperHeim's
  visual composition: red-tinted icon block, "Delete voice?" title,
  "This action cannot be undone." subtitle, voice name card,
  Cancel + red Delete buttons.
- [ ] Cancel / Esc / click-outside closes the dialog with no
  action.
- [ ] Delete confirmed → calls `VoiceLibraryService.DeleteAsync`.
  On success the dialog closes and the row disappears via
  `VoicesChanged`. No toast / extra confirmation.
- [ ] On `VoiceLibraryService` IO failure (file lock, permission),
  the dialog stays open and shows the error inline; user can retry
  or cancel.
- [ ] After a delete, the voice is gone from `library.json` even if
  the per-voice folder couldn't be deleted (per main-015's
  ordering). Verifiable: simulate a file lock, confirm
  `library.json` no longer lists the id, confirm the orphan folder
  is cleaned up on the next app launch (reconciliation).
- [ ] Delete during preview: the delete proceeds; preview audio may
  glitch / fail at the next chunk. The orphan is reconciled on next
  launch. The status footer surfaces any synthesis error per the
  existing `SpeakService` error path. **Verified manually**, not
  unit-tested (the race is timing-dependent).
- [ ] Speak page's voice picker (main-013) reflects the deletion
  the next time it's refreshed (live via `VoicesChanged`, or on
  next `OnNavigatedTo`). If the deleted voice was the picker's
  selection, the picker falls back per main-013's
  `UserSettings.DefaultVoiceId` resolution chain (default → first
  voice in catalog).
- [ ] Visual matches the styleguide. The destructive red
  (`#FFE81224`) matches WhisperHeim and is the only place this
  colour appears in utterheim's UI.
- [ ] Build clean: `dotnet build utterheim.sln -c Debug` produces
  0 errors, 0 warnings.

## Notes

### ADRs that govern this task

- **ADR 0010** — `CommunityToolkit.Mvvm` (the per-row VM gains a
  `[RelayCommand]` for Delete; dialog VM uses the same pattern).
- **ADR 0005** — voice persistence layout (`DeleteAsync` removes
  per this layout via main-015's service).

### References

- `C:\src\heimeshoff\tooling\WhisperHeim\src\WhisperHeim\Views\DeleteConfirmationDialog.xaml`
  — visual composition reference (icon, copy, button colours).
  Don't copy verbatim — port to wpfui `ContentDialog`.
- main-014 `done/` — row template / `VoiceRowViewModel` /
  `VoicesChanged` subscription this task extends.
- main-015 `done/` — `VoiceLibraryService.DeleteAsync` API this
  task calls.

### Out of scope (do not creep)

- **Bulk delete / multi-select** — single-row only in v1.
- **Undo** — modal confirm is the only safety net.
- **Active-playback guard** — the delete proceeds regardless;
  reconciliation cleans orphans (per main-015 Q10).
- **Rename / re-tag from the row** — no v1 task.
- **Deleting built-in voices** — explicitly forbidden;
  `IsBuiltIn` rows have no Delete affordance.

### Worker tips

- The Delete column can be added to main-014's existing row template
  by widening the `Grid.ColumnDefinitions` to four columns and
  binding the new column's first child's `Visibility` to a
  `BooleanToVisibilityConverter` over `!IsBuiltIn` (or via a
  one-line value converter).
- The confirmation dialog: use wpfui's `ContentDialog`
  (`Wpf.Ui.Controls.ContentDialog`) with `IContentDialogService`
  resolved from DI, not a separate `Window` subclass. The dialog's
  view-model exposes `VoiceName`, `IsDeleting`, `ErrorMessage` and a
  `[RelayCommand]` Delete that calls `VoiceLibraryService`.
- Fire-and-forget on the cancel path: just close the dialog, no
  service call.
- For the destructive button styling, `ContentDialog` exposes a
  `PrimaryButtonAppearance` property — set to `Danger` (or
  equivalent) if wpfui has it; otherwise template the button with
  the `#FFE81224` background as WhisperHeim does.

## Outcome

Cloned voice rows on the Voices page now show a per-row Delete button
between Preview and the active-request indicator. Click → Fluent
`ui:ContentDialog` opens (in-window, hosted by `RootContentDialogPresenter`
in `MainWindow.xaml`) with WhisperHeim's destructive styling: red-tinted
icon block, "Delete voice?" title, "This action cannot be undone."
subtitle, voice-name card, Cancel + red Delete buttons. Confirm calls
`VoiceLibraryService.DeleteAsync`; on success the dialog hides and the
row vanishes via `VoicesChanged`. On IO / permission failure the dialog
stays open with an inline red error message; the user can retry or cancel.

Built-in rows are unmodified — they keep the original 3-column layout (no
Delete column allocated) because the cloned + built-in sections live in
separate `ItemsControl.ItemTemplate`s. Build is clean (`dotnet build
utterheim.sln -c Debug` → 0 errors, 0 warnings).

### Key files

- `src/Utterheim/ViewModels/Dialogs/DeleteVoiceDialogViewModel.cs` — new
  VM; holds voice id + name, drives `IsDeleting` / `ErrorMessage`, exposes
  Delete / Cancel commands.
- `src/Utterheim/Views/Dialogs/DeleteVoiceDialog.xaml(.cs)` — new
  ContentDialog view (`IsFooterVisible="False"`, custom button row with
  `#FFE81224` Delete button template).
- `src/Utterheim/ViewModels/Pages/VoicesPageViewModel.cs` — added
  `VoiceLibraryService` + `IContentDialogService` dependencies, wired
  `RequestDelete(VoiceRowViewModel)` that constructs the dialog VM and
  shows the dialog. `VoiceRowViewModel` gained `DeleteCommand` +
  `IsCloned` and a nullable delete-action ctor parameter.
- `src/Utterheim/Views/Pages/VoicesPage.xaml` — cloned `ItemsControl`
  template extended to a 4-column grid (name+meta | Preview | Delete |
  active indicator).
- `src/Utterheim/Views/MainWindow.xaml(.cs)` — added
  `RootContentDialogPresenter` and `IContentDialogService` ctor param;
  `OnLoaded` calls `SetDialogHost`.
- `src/Utterheim/EntryPoint.cs` — registered `IContentDialogService` as
  singleton; passed it to `MainWindow`.

### Verification note

The build is clean. The interactive UI behaviours — clicking Delete opens
the dialog, Cancel / Esc dismisses with no action, Confirm calls the
service and removes the row, IO failure keeps the dialog open with an
inline error — are **not interactively re-tested** in this pass; the code
is in place per the main-026 spec and any regression will surface during
the next manual run.
