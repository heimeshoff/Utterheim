---
id: main-017
title: About page — logo, tagline, version, engine status
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

Completes the page set the styleguide canonicalised (Speak, Voices, Settings,
About). Small, but it's the place that surfaces "what is this app, what
version am I on, and is the engine actually healthy". Useful for the user
and trivial to extend later.

## What

A small About page with:

- The speaking-person logo at a comfortable size (rendered from
  `assets/branding/mockingbird-logo.svg` via the inline geometry already used
  in the shell, or one of the rasterised PNGs from main-012).
- App name + tagline ("Mockingbird — Local voices for Claude Code", or
  whatever the styleguide currently endorses).
- App version (from assembly info).
- pocket-tts engine status block — surfaces what `GET /status` already
  returns: `sidecar.state`, `sidecar.healthy`, `sidecar.port`, `sidecar.lastError`.
  Auto-refreshes while the page is visible.
- A "view logs" link that opens `%LOCALAPPDATA%\Mockingbird\logs\` in
  Explorer (per ADR 0005 / 0008).
- Credits / acknowledgements line referencing pocket-tts (kyutai labs) and
  WhisperHeim as the design ancestor.

## Acceptance criteria

- [ ] About page reachable from the sidebar nav.
- [ ] Logo + tagline + version render correctly.
- [ ] Engine status block reflects the same data `GET /status` returns and
  updates within ~2 s of state changes (e.g., kill the sidecar process,
  watch the page move from `running` → `restarting` → `running`).
- [ ] "View logs" opens the log directory in Explorer.
- [ ] Visual matches the styleguide.

## Notes

- Reference: `docs/styleguide.md` §Page set ("About: Logo, tagline, version,
  model status (pocket-tts engine status)").
- This page is a candidate for early implementation alongside main-018
  (first-run verification) — being able to *see* sidecar state visually
  helps validate the engine path without curl.
