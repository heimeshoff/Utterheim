---
id: main-016
title: Settings page — output device, startup, hotkey, paths
status: backlog
type: feature
context: main
created: 2026-05-01
completed:
commit:
depends_on: [main-010, main-020]
blocks: []
tags: [frontend, page]
---

## Why

The walking skeleton hard-codes everything: default WaveOut device, port 7223,
double-tap LCtrl with the window from `appsettings.json`, data path picked
once on first run. The user needs a UI to change these without editing JSON
files. Mirrors WhisperHeim's General settings page.

## What

A Settings page with at least:

- **Output device** — dropdown of available WaveOut devices, persists choice,
  takes effect on next utterance (or immediately, TBD).
- **Start minimised** — checkbox, persists.
- **Launch at startup** — checkbox, writes/removes the
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry (WhisperHeim
  pattern).
- **HTTP port** — read-only display of the active port (default 7223), with
  a note that changing it requires a restart. Editable field is a stretch
  goal — refine whether v1 needs it.
- **Stop hotkey** — display of the current gesture (double-tap LCtrl). v1 keeps
  it fixed per ADR 0006; rebinding UI is out of scope.
- **Data path** — display of the active path (read from `bootstrap.json`
  per ADR 0005). "Open in Explorer" button. Changing the path is out of
  scope for v1 (would require a migration flow).

Persisted state extends the `UserSettings` service introduced in main-013
(`Services\Settings\UserSettings.cs`, backed by
`%LOCALAPPDATA%\Mockingbird\settings.json`). The JSON schema is
forward-compatible — add fields, don't fork the file.

## Acceptance criteria

- [ ] Settings page reachable from the sidebar nav.
- [ ] Output device dropdown lists real WaveOut devices and selecting one
  routes subsequent playback to that device.
- [ ] Start minimised + Launch at startup checkboxes persist across restart
  and behave as labelled.
- [ ] Active HTTP port is shown (whatever the live `SpeakServer` is bound to).
- [ ] Active stop-hotkey gesture is shown (read-only for v1).
- [ ] Active data path is shown with an "Open in Explorer" affordance.
- [ ] Visual matches the styleguide.

## Notes

- Reference: WhisperHeim `design.md` General settings section, ADR 0005
  (path layout), ADR 0006 (hotkey), ADR 0008 (cross-cutting concerns
  including settings).
- Out of scope for v1: hotkey rebinding, data-path migration, port editing
  with auto-restart. Capture as separate refinement tasks if the user wants
  any of these.
- Open question for refinement: does Settings carry the "voice-per-Claude-session"
  routing UI? The vision implies sessions identify themselves somehow when
  calling `/speak` — but the current `/speak` payload is just `{text, voice}`,
  so routing is the caller's concern. Probably out of scope here; flag during
  refinement.
- **Forward-link from main-013 (Speak page refinement, 2026-05-01):** the
  storage layer for `DefaultVoiceId` (`Services\Settings\UserSettings.cs`,
  backed by `%LOCALAPPDATA%\Mockingbird\settings.json`) **already ships in
  main-013** along with the Speak page reading it. This Settings page must
  add the **UI** to mutate the value: a "Default voice" dropdown sourced
  from `VoiceCatalog`, with selection writing back through `UserSettings`.
  Schema is forward-compatible by design — main-016 just extends the same
  JSON file with the other settings slots (output device, start-minimised,
  launch-at-startup, etc.). Subscribe to `UserSettings.DefaultVoiceIdChanged`
  if any other surface needs to react.
- Persisted state: extend `Services\Settings\UserSettings.cs` (introduced in
  main-013) rather than introducing a new settings file. The forward-compat
  JSON shape was set up specifically so main-016 can add fields without
  migration.
