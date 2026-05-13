# 0021. Engine diagnostics live on Settings, not About

Date: 2026-05-05
Status: Accepted
Context: main / main-032

## Context

main-017 originally placed the engine-status panel (state pip, port, healthy
indicator, last-error block, Restart Engine button) and the View-logs link on
the **About** page, alongside the brand mark, version, and credits. At the
time it was the most visible non-stub destination for the richer status
surface; the four-page nav shell (main-020) had just landed and Settings
(main-016) was equally young, so the two pages competed for "where does
diagnostics go".

Two consequences of that placement surfaced over the next handful of tasks:

1. The About page accumulated two distinct purposes — *identity* (who built
   this, what version, where to find help) and *diagnostics* (is the engine
   alive, can I restart it, where are the logs). When main-029 reskinned
   pages WhisperHeim-style and main-030 introduced the `BrandHeroControl`,
   the identity surface wanted to expand (profile card, support links,
   Ko-fi button, GitHub link) while the diagnostics surface wanted to stay
   exactly where it was. Two intentions, one page — wrong shape.
2. Users intuit "settings" as where you tweak app behaviour (output device,
   data path, hotkeys) **and** where you investigate behaviour (HTTP port,
   engine state, last error). WhisperHeim's About stays purely identity; its
   Settings-equivalent owns model-status diagnostics. Utterheim drifted from
   that pattern by accident, not by deliberate choice.

main-032 corrects the placement by relocating diagnostics to the bottom of
Settings → Diagnostics (after HTTP port + Stop hotkey + Data path) and by
making About a pure identity surface that mirrors WhisperHeim's About
section-for-section.

## Decision

**Engine-status diagnostics belong on Settings → Diagnostics. About is
identity / credits only.**

Concretely:

- The engine-status card (state pip + port + healthy + last error + Restart
  Engine) and the "View logs" hyperlink move to the end of Settings →
  Diagnostics, beneath Data path.
- The About page becomes a pure identity surface: hero (logo + name +
  version + tagline), profile-and-contact card (Marco's bio + Get-in-Touch
  links), Ko-fi / GitHub support card, credits.
- About moves to `ui:NavigationView.FooterMenuItems` so it sits at the
  bottom-left of the nav pane like WhisperHeim — visually separating
  "everyday workflow" (Speak / Voices / Settings in `MenuItems`) from
  "about this app" (About in `FooterMenuItems`).

The persistent footer (HTTP + Engine state, backed by `EngineStatusViewModel`)
**stays unchanged**. The dual surface — always-visible footer tag + richer
panel-on-demand on Settings — was deliberate per main-017 Q2; relocating the
panel to Settings doesn't change that calculus.

## Consequences

### Code shape

`EngineStatusCardViewModel` is extracted from the former `AboutPageViewModel`
into its own VM under `ViewModels/` (parallel to `Pages/`, not inside it —
this VM is composed, not a page VM). Composed onto
`SettingsPageViewModel.EngineStatus`, registered transient in DI so each
Settings-page resolution gets a fresh instance — the same lifetime
`VoiceCloningViewModel` uses for the same reason (mirrors main-025's
composition pattern). The `Attach()` / `Detach()` lifecycle that gates the
`SidecarHost.StateChanged` subscription is now invoked from
`SettingsPage.OnNavigatedTo` / `OnNavigatedFrom` via the parent VM's
`Attach()` / `Detach()`.

`AboutPageViewModel` collapses to a single `Version` property sourced from
`AppInfo.Version`. No commands, no event subscriptions, no `Attach()` /
`Detach()`. The page code-behind handles the static external links (Ko-fi
button click, hyperlink-navigate handler) directly because the URLs are
app-wide constants on `AppInfo` (`KofiUrl`, `GithubUrl`), not per-instance VM
values.

### When this might want revisiting

If a second engine ships and engine selection becomes a per-session decision,
that surface *might* belong on the Speak page rather than Settings (per-task
selection, like the voice picker). Today there's only pocket-tts, so engine
status is global app health and Settings is the right home.

If the diagnostics surface keeps growing (logs viewer in-app, packet capture,
throughput meter), it might split out into its own Diagnostics page. v1
crosses that bridge if it arrives — the relocation here costs nothing if the
section later re-homes.

### Backward compatibility

None required — Utterheim is single-user, single-install, no settings
schema touched. The user's mental map of "where do I see if the engine is
healthy?" updates the moment they open Settings → Diagnostics, and the
persistent footer always shows the engine label so the signal never
disappears.

## Related

- main-017 — placed the panel on About originally. Q2 (footer vs richer
  panel) is the precedent for the dual-surface choice, which we preserve.
- ADR 0018 — in-process status data flow (subscribe to
  `SidecarHost.StateChanged`, don't call `GET /status`). Unchanged; the
  subscription just moves to the new sub-VM.
- main-025 — `VoiceCloningViewModel` composition pattern this follows.
- main-032 — the task that made this change.
