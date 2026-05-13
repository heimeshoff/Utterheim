# ADR 0020 — Runtime data-path change is a pointer swap, not a migration

Date: 2026-05-05
Status: Accepted
Context: main
Related: ADR 0005 (voice persistence layout), ADR 0006 (WhisperHeim reuse)

## Context

The Settings page (main-031) lets the user pick a new data folder via a
folder dialog. The question is how much of the existing on-disk state
follows the user when they pick a new folder.

Two extremes:

1. **Full migration** — copy/move every relevant file under the old
   `<dataPath>` to the new location, then atomically flip the pointer.
   Any failure mid-flight needs rollback to avoid a half-migrated tree.
2. **Pointer-only swap** — flip `bootstrap.json`'s `dataPath` field, fire
   `DataPathChanged`, leave the old folder untouched. The user is free to
   copy files manually if they want continuity.

WhisperHeim — the sibling app this pattern is adapted from per ADR 0006 —
chose option 2.

## Decision

**Mockingbird does the same: pointer-only swap, no migration.**

Concretely, on a successful `DataPathService.SetDataPath(newPath)`:

- `bootstrap.json` is rewritten with the new override (via temp+rename).
- `DataPathChanged` fires with the resolved path.
- `VoiceLibraryService.LoadAsync` re-runs against the new
  `<newPath>\voices\` folder, which is typically empty on first swap.
- The old `<oldPath>\voices\` folder stays exactly where it was. Nothing
  is moved, copied, or deleted.
- A MessageBox info ("Restart Mockingbird for the change to take full
  effect") nudges the user to cycle the app so other path-anchored
  surfaces (logs, etc.) are unambiguous, but the live `LoadAsync`
  ensures the Voices page reflects the swap immediately.

The Reset path (`SetDataPath(null)`) follows the same pattern: the
override is cleared, the resolved path falls back to
`%APPDATA%\Mockingbird\`, and any voices that were created under the
override stay where they are.

## Why pointer-only

1. **Robustness** — a partial migration is the worst-case state: the
   user has a half-copied tree, an inconsistent `library.json`, and a
   pointer that may or may not match either side. Pointer-only has no
   intermediate states. The failure mode is "voices appear empty in the
   new location, but the old folder is fully intact" — recoverable by
   pointing the path back.
2. **Scope** — Mockingbird is a personal tool with one user and small
   data sets (single-digit-MB voice profiles). The "I just want to put
   my voices on OneDrive" use case is well-served by the user
   manually copying `<oldPath>\voices\` to `<newPath>\voices\` before
   flipping the pointer if they care about continuity.
3. **Sibling consistency** — WhisperHeim already does this. Diverging
   would require justification we don't have. The two apps share the
   bootstrap.json discipline (ADR 0005); they should share the runtime
   change discipline too.
4. **`bootstrap.json` itself doesn't move** — it stays at
   `%APPDATA%\Mockingbird\bootstrap.json` because that's where the
   pointer lives. Putting the pointer inside the path it points at would
   be a self-reference loop.
5. **Only `<dataPath>\voices\` relocates in Mockingbird.** Per the
   layout in ADR 0005, every other directory (`runtime/`, `models/`,
   `cache/`, `logs/`, `bootstrap-state.json`, `settings.json`) is
   anchored to `LocalRoot` (`%LOCALAPPDATA%\Mockingbird\`) and is
   genuinely indifferent to a `dataPath` change. So the migration
   surface is small — it's *just* the voices folder — but the pointer
   semantics still beat copying it.

## Consequences

- **Old voices are abandoned at the previous path on swap.** They
  remain readable on disk; the user can recover them by either pointing
  the path back, or by manually copying the `voices\` subtree across.
  Documented inline in the Settings card: *"Existing voices stay at the
  old location."*
- **The Voices page may show an empty Cloned section after a swap.**
  This is expected and self-explanatory once the user reads the inline
  tip on the card.
- **The runtime live-reload is partial.** `VoiceLibraryService` reloads
  immediately, so the Voices page reflects the new path without
  re-navigation. But anything else that was path-derived at startup
  (e.g. the file-logger's path on disk if it ever moves under
  `<dataPath>` in a future release) won't follow until restart. Hence
  the MessageBox "Restart for full effect."
- **Validation is writability-only.** `ValidatePath` round-trips a tiny
  temp file. We do not check whether the target already contains a
  Mockingbird tree, or refuse paths that would shadow LocalRoot. Edge
  cases like "user picks `C:\Windows\System32`" surface as a
  `ValidatePath` failure (no write access) rather than a structural
  rejection — keeps the v1 surface small.

## Out of scope (deferred)

- Active migration ("copy old voices to new location") — could become a
  one-shot button on the Settings card if multiple users ask for it.
  No task open as of 2026-05-05.
- "Are you sure you want to abandon old voices?" confirmation dialog —
  deliberately omitted, matching WhisperHeim. The inline card tip plus
  the post-swap MessageBox info is sufficient signal.
- UNC-path / network-drive special casing beyond the writability test.
- Per-Claude-session data-path override.
