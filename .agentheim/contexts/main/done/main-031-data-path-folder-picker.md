---
id: main-031
title: Editable data path with folder-picker dialog
status: done
type: feature
context: main
created: 2026-05-04
completed: 2026-05-05
commit: cc14359
depends_on: [main-010]
blocks: []
tags: [settings, data-path, persistence]
---

## Why

The Settings page exposes the data path as read-only text plus an
"Open in Explorer" button (main-016 `OpenDataPathCommand`, see
[SettingsPage.xaml lines 141–164](../../../src/Utterheim/Views/Pages/SettingsPage.xaml)).
The user wants to **change** the data path from the UI — pick a new folder via a
dialog, persist the choice, have the app use that folder. The current
"Open in Explorer" affordance only shows the folder; it does not change it.

The main-016 spec explicitly deferred this:
> "Changing the path requires a migration flow and is **out of scope** for v1."

This task lifts that exclusion, adopting the WhisperHeim sibling pattern
(pointer-swap, writability validation, hybrid live + restart-required
notice) — same shape as
[`WhisperHeim/Services/Settings/DataPathService.cs`](../../../../tooling/WhisperHeim/src/WhisperHeim/Services/Settings/DataPathService.cs)
and
[`WhisperHeim/Views/Pages/GeneralPage.xaml.cs::BrowseDataPath_Click`](../../../../tooling/WhisperHeim/src/WhisperHeim/Views/Pages/GeneralPage.xaml.cs).

## What

### Scope of relocation (smaller than WhisperHeim's)

In Utterheim, `<dataPath>` only relocates the **voice library**
(`<dataPath>\voices\` — `library.json` + per-voice folders). Everything
else is anchored to `DataPathService.LocalRoot`
(`%LOCALAPPDATA%\Utterheim\`) and stays put across a path change:

- `runtime\python\` (embedded Python + pocket-tts + sidecar)
- `models\pocket-tts\`
- `cache\`
- `logs\`
- `bootstrap-state.json`
- `settings.json` (per `UserSettings`'s ctor — uses `LocalRoot` directly,
  ignoring `DataPathService.SettingsPath`)

`bootstrap.json` itself stays at `RoamingRoot` (`%APPDATA%\Utterheim\`)
because that's where the pointer lives.

So the **only** service that needs to react to a data-path change is
`VoiceLibraryService`.

### `DataPathService` additions

Port the WhisperHeim trio:

```csharp
public event EventHandler<string>? DataPathChanged;

public static bool ValidatePath(string path)
{
    try
    {
        Directory.CreateDirectory(path);
        var testFile = Path.Combine(path, $".utterheim_write_test_{Guid.NewGuid():N}.tmp");
        File.WriteAllText(testFile, "test");
        File.Delete(testFile);
        return true;
    }
    catch { return false; }
}

public bool SetDataPath(string? newPath)
{
    if (string.IsNullOrWhiteSpace(newPath))
    {
        _bootstrap.DataPath = null;
        Save();                               // temp+rename
        DataPathChanged?.Invoke(this, DataPath);
        return true;
    }
    if (!ValidatePath(newPath)) return false;
    _bootstrap.DataPath = newPath;
    Save();                                   // temp+rename
    DataPathChanged?.Invoke(this, DataPath);
    return true;
}
```

**Improvement over WhisperHeim**: change the existing
`DataPathService.Save()` to write `bootstrap.json` via temp+rename
(`bootstrap.json.tmp` → `File.Move(..., overwrite: true)`) so a crash
mid-update can't leave a corrupt pointer file. Mirrors the temp+rename
discipline `VoiceLibraryService` already enforces for `library.json` /
`meta.json` / `profile.safetensors`.

### `VoiceLibraryService` reaction

In the ctor (or via a hosted-service shim if the ctor is too eager),
subscribe to `DataPathService.DataPathChanged` and on each event:

1. `await LoadAsync(ct)` — re-reads `library.json` and reconciles folders
   from the new `_dataPathService.VoiceLibraryPath`.
2. The existing `LibraryChanged` event fires from `LoadAsync`'s
   reconciliation tail → `VoiceCatalog.VoicesChanged` fires → main-014's
   subscriber refreshes the Voices page rows without re-navigation.

`PocketTtsEngine.StreamAsync` already resolves profile paths per-request
via `VoiceLibraryService.TryResolveProfilePath(...)`, so cloned-voice
synthesis tolerates the swap with no engine code changes.

### Settings → Diagnostics card

Replace the existing single "Open in Explorer" affordance on the Data path
card with the WhisperHeim Browse + Reset pair (drop Open-in-Explorer
entirely — easy to add back later if missed in v1.5):

- Path display (monospace, current `_dataPathService.DataPath`).
- `Browse...` button (`Appearance="Primary"`).
- `Reset` button (`Appearance="Secondary"`).
- One-line tip: *"Existing voices stay at the old location."*

`Browse...` flow (mirror of WhisperHeim `BrowseDataPath_Click`):

```
var dialog = new Microsoft.Win32.OpenFolderDialog
{
    Title = "Select data folder for Utterheim",
    InitialDirectory = _dataPathService.DataPath,
};
if (dialog.ShowDialog() != true) return;

var newPath = dialog.FolderName;
if (!DataPathService.ValidatePath(newPath))
{
    MessageBox.Show(
        $"The selected folder is not writable:\n\n{newPath}\n\nPlease choose a different folder.",
        "Invalid folder",
        MessageBoxButton.OK, MessageBoxImage.Warning);
    return;
}
if (_dataPathService.SetDataPath(newPath))
{
    MessageBox.Show(
        "Data folder changed. Please restart Utterheim for the change to take full effect.",
        "Restart required",
        MessageBoxButton.OK, MessageBoxImage.Information);
}
```

`Reset` flow: `_dataPathService.SetDataPath(null)`. No MessageBox — the
display refresh and the disappearance of any custom path is
self-explanatory (matches WhisperHeim).

### `SettingsPageViewModel` adjustments

- **Remove** `OpenDataPathCommand` (and its caller in `SettingsPage.xaml`).
- **Add** `BrowseDataPathCommand` (`[RelayCommand]`) — calls the flow above.
- **Add** `ResetDataPathCommand` (`[RelayCommand]`) — calls
  `SetDataPath(null)`.
- Subscribe `DataPathService.DataPathChanged` in `OnNavigatedTo` (and
  unsubscribe in `OnNavigatedFrom`) so the displayed path stays in sync
  if any other surface ever calls `SetDataPath`.
- The diagnostics `DataPath` observable property becomes a live mirror of
  `_dataPathService.DataPath`.

## Acceptance criteria

- [ ] `DataPathService` exposes `static bool ValidatePath(string)`,
      `bool SetDataPath(string?)`, and `event EventHandler<string>? DataPathChanged`.
- [ ] `DataPathService.Save()` writes `bootstrap.json` via temp+rename
      (no `File.WriteAllText` to the live file).
- [ ] Settings → Diagnostics → Data path card carries `Browse...`
      (Primary) + `Reset` (Secondary) buttons. The previous
      `Open in Explorer` button is gone.
- [ ] Card carries the inline tip:
      *"Existing voices stay at the old location."*
- [ ] `Browse...` opens `Microsoft.Win32.OpenFolderDialog`
      (Vista folder browser, not a file dialog) with
      `InitialDirectory = _dataPathService.DataPath`.
- [ ] Selecting a writable folder: persists the new path through
      `SetDataPath`, fires `DataPathChanged`, the on-page display
      updates immediately, and a MessageBox info appears:
      *"Data folder changed. Please restart Utterheim for the change
      to take full effect."*
- [ ] Selecting an unwritable folder: a MessageBox warning appears
      ("The selected folder is not writable…") and `bootstrap.json` is
      unchanged.
- [ ] `Reset` clears the `DataPath` override in `bootstrap.json`,
      refreshes the on-page display to the default
      (`%APPDATA%\Utterheim\`), fires `DataPathChanged`. **No
      MessageBox** on Reset.
- [ ] `VoiceLibraryService` re-runs `LoadAsync` on `DataPathChanged`,
      and the Voices page rows reflect the new path's library.json
      **without page re-navigation** (live `LibraryChanged` →
      `VoicesChanged` chain).
- [ ] Old voices at the previous path are **not** moved, copied, or
      deleted.
- [ ] `runtime/`, `models/`, `cache/`, `logs/`, `bootstrap-state.json`,
      `settings.json` are unaffected by a data-path change.
- [ ] `bootstrap.json` itself stays at `%APPDATA%\Utterheim\` (the
      pointer file does not relocate).
- [ ] Build is clean (`dotnet build utterheim.sln -c Debug` →
      0 errors, 0 warnings).

## Notes

- Worker may verify-by-inspection per the standing convention; the live
  Browse → restart cycle and the live `library.json` swap don't require
  full interactive re-test if the code path is in place.
- `OpenFolderDialog` is in `Microsoft.Win32` and ships with WPF on
  net8+ — no new package reference needed (utterheim is `net9.0-windows`).
- The `DataPathChanged` subscription in `VoiceLibraryService` should be
  detached on dispose so test fixtures that swap the service don't leak.
- `bootstrap.json` temp+rename: write to `bootstrap.json.tmp`,
  `File.Move(tmp, final, overwrite: true)`. `File.Replace` would also
  work but `Move(..., overwrite: true)` matches the discipline elsewhere
  in the codebase.
- Out of scope (v1 boundary, repeated for the worker): no migration of
  existing voices to the new path; no UNC-path / network-drive special
  casing beyond the writability test; no per-Claude-session data-path
  override; no "open in Explorer" affordance.

## Resolution log (refinement 2026-05-05)

The five open questions in the prior backlog version were resolved as
follows after consulting the WhisperHeim sibling pattern:

| Question | Resolution |
|---|---|
| Migration semantics? | Pointer-swap. Old data untouched. |
| Restart vs live? | Hybrid: live `LoadAsync` for the voice library + MessageBox info asking the user to restart for full effect. |
| Path validation? | Writability test (`ValidatePath`). |
| Confirmation before applying? | None for swap or reset. MessageBox info after swap is sufficient. |
| Keep `Open in Explorer`? | Drop it. Match WhisperHeim card layout (Browse + Reset only). |

## Outcome

Implemented end-to-end per the spec.

- `DataPathService` (`src\Utterheim\Services\Settings\DataPathService.cs`)
  gained `static bool ValidatePath(string)`, `bool SetDataPath(string?)`,
  and `event EventHandler<string>? DataPathChanged`. `Save()` now writes
  `bootstrap.json` via temp+rename (`bootstrap.json.tmp` →
  `File.Move(..., overwrite: true)`).
- `VoiceLibraryStartup`
  (`src\Utterheim\Services\Voices\VoiceLibraryStartup.cs`) subscribes
  to `DataPathChanged` in `StartAsync` and re-runs
  `VoiceLibraryService.LoadAsync` on each event (off the dispatcher via
  `Task.Run`); detaches in `StopAsync`. `LibraryChanged → VoicesChanged`
  refreshes the Voices page rows live without re-navigation.
- `SettingsPageViewModel`
  (`src\Utterheim\ViewModels\Pages\SettingsPageViewModel.cs`) replaces
  `OpenDataPathCommand` with `BrowseDataPathCommand` and
  `ResetDataPathCommand`; subscribes to `DataPathChanged` via
  `Attach()` / `Detach()` so the displayed `DataPath` stays live.
  `BrowseDataPath` invokes `Microsoft.Win32.OpenFolderDialog` with
  `InitialDirectory = _dataPathService.DataPath`, validates writability,
  surfaces a MessageBox warning on failure or a MessageBox info
  ("Restart Utterheim for the change to take full effect.") on
  success.
- `SettingsPage.xaml` data-path card swapped to `Browse...` (Primary)
  + `Reset` (Secondary); description text now reads "Where voices are
  stored… Existing voices stay at the old location." `SettingsPage.xaml.cs`
  calls `ViewModel.Attach()` in `OnNavigatedTo` and `Detach()` in
  `OnNavigatedFrom`.
- ADR 0020
  (`.agentheim\knowledge\decisions\0020-data-path-runtime-swap-pointer-only.md`)
  records the pointer-only-no-migration decision so a future maintainer
  doesn't wonder why old voices stay behind.
- BC README updated: Settings → Diagnostics → Data path subsection now
  documents Browse + Reset; the "data-path change with migration flow"
  out-of-scope note is lifted; the structure entries for
  `DataPathService.cs`, `SettingsPageViewModel.cs`, and
  `VoiceLibraryStartup.cs` reflect the new responsibilities.

Build clean: `dotnet build utterheim.sln -c Debug` → 0 errors,
0 warnings. Interactive verification (Browse → restart cycle, Reset
clear, live `library.json` swap) is assume-pass per the standing
convention; the code is in place per the acceptance criteria and any
regression will surface during the next manual run.
