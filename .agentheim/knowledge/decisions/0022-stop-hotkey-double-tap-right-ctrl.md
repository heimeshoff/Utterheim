---
id: 0022
title: Stop hotkey watches Right Ctrl (double-tap), not Left Ctrl
scope: main
status: accepted
date: 2026-05-05
supersedes: []
superseded_by: []
related_tasks: [main-033, main-004]
related_research: []
---

# ADR 0022: Stop hotkey watches Right Ctrl (double-tap), not Left Ctrl

## Context
ADR 0006 established the copy-and-modify reuse pattern from WhisperHeim and
mentioned a "double-tap LCtrl" gesture in passing. Mockingbird shipped that
through main-006 / main-009 and lived with Left Ctrl through main-032. main-033
revisited the choice based on real-world use:

- Left Ctrl sits at the bottom-left corner of every Windows keyboard. On split
  / staggered / tenkeyless layouts it is reachable, but it is also the same key
  the user's left hand spends most of its time anchored to (Ctrl+C, Ctrl+V,
  Ctrl+chording in editors). Reserving "double-tap" semantics on a key whose
  single-tap rate is *very high during normal work* increases the chance of an
  accidental stop.
- Right Ctrl sits to the right of the spacebar on most keyboards, next to the
  arrow cluster. It is far less frequently used as a chord modifier: most apps
  do not bind anything Right-Ctrl-specific (the OS treats Right and Left Ctrl
  as the same VK_CONTROL for chording). That makes a double-tap of *Right*
  Ctrl feel like a discoverable, dedicated gesture rather than a re-purposed
  modifier.
- Marco's typing posture: the right hand floats over the cursor / mouse;
  reaching Right Ctrl with the right thumb / pinky to "double-tap stop"
  matches the muscle memory of "I want to interrupt the audio I'm hearing"
  more naturally than reaching across the keyboard with the left hand mid-task.

## Decision
The mockingbird stop hotkey is **double-tap Right Ctrl** (within 400 ms).

- `DoubleTapDetector` defaults to `NativeMethods.VK_RCONTROL`.
- `EntryPoint` registers it with `VK_RCONTROL` explicitly.
- All user-facing strings ("Double-tap Right Ctrl") and documentation (BC
  README, repo-top README, settings card label) match.
- `appsettings.json` carries `"DoubleTapKey": "RCtrl"` for documentary value
  (the field is currently descriptive only; it is not parsed back into the
  detector's virtual-key code).

The 400 ms double-tap window is unchanged. Drain-vs-keep semantics
(ADR 0004) are unchanged.

## Consequences
### Positive
- Lower accidental-stop rate during normal Ctrl-chord work.
- Right-handed reach matches the "interrupt audio" mental model.
- Keeps the gesture distinct from anything WhisperHeim binds (WhisperHeim
  uses chord-style hotkeys, not double-tap, so no clash either way — but
  this also keeps the two apps' semantics visibly different).

### Negative
- Some compact keyboards (60% layouts, certain laptops) drop the right-side
  Control key entirely. On those machines the gesture is unreachable and
  the user must fall back to tray Stop or `POST /stop`. We accept this — a
  rebinding UI is still out of scope per ADR 0006 v1 boundary.
- Behavioural change for existing users who learned the Left-Ctrl gesture
  in earlier builds. The change is announced via the Settings → Stop hotkey
  card label, which now reads "Double-tap Right Ctrl".

## Alternatives considered
- **Make the watched key configurable in `appsettings.json`.** Out of scope
  for v1 per ADR 0006 ("no rebinding UI in v1"). Hard-coding the default
  with a one-line change is sufficient for now; a settings-driven binding
  is a future refinement when there is more than one valid choice.
- **Switch to a chord (e.g. Ctrl+Shift+Space).** Chords compete with editor
  / IDE bindings; the double-tap gesture has the advantage of being
  modifier-free and globally unique-feeling. Keep the gesture, change the
  key.
- **Watch both Left and Right Ctrl.** Doubles the accidental-stop surface
  area while erasing the discoverability win above. Rejected.
