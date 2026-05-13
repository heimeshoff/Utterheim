---
id: 0003
title: Expose mockingbird's speak endpoint over loopback HTTP (JSON)
scope: global
status: accepted
date: 2026-05-01
supersedes: []
superseded_by: []
related_tasks: [main-003, main-009]
related_research: []
---

# ADR 0003: Expose mockingbird's speak endpoint over loopback HTTP (JSON)

## Context
Claude Code triggers TTS via shell hooks. Multiple parallel Claude sessions can fire hooks concurrently. The vision's "speak endpoint" is the BC's published interface (see `contexts/main/README.md`). Three transports are viable: localhost HTTP, named pipe, or CLI invocation. End-to-end budget is ≤2 s; transport overhead must not dominate. There is no auth requirement (single-user, localhost-only, by design).

## Decision
The speak endpoint is an HTTP/1.1 server bound to `127.0.0.1:7223` (default, override via settings) inside the mockingbird tray process. Endpoints (v1):

- `POST /speak` — body `{"text": "...", "voice": "<id>"}`. Response: `202 Accepted` with `{"requestId":"<uuid>","queuePosition":N}`. Synthesis and playback happen asynchronously; the caller does not block on audio.
- `POST /stop` — drains current utterance per ADR 0004. Response: `200 OK`.
- `GET /voices` — list of `{id, name, engine, isBuiltIn}`.
- `GET /status` — current playback state, queue length, sidecar health.

The wire format is the published language. Bind loopback-only; no token/auth in v1. A `mockingbird-speak` CLI wrapper ships alongside the tray exe so Claude hooks can call `mockingbird-speak --voice alba "task done"` without remembering curl syntax.

## Consequences
### Positive
- Trivially concurrent (Kestrel handles N parallel POSTs out of the box).
- Hook authors can use curl directly or our CLI wrapper.
- Easy to extend (SSE stream of progress events, voice CRUD endpoints).
- Inspectable: any HTTP client can poke at it for debugging.

### Negative
- A few ms per request vs named pipe (negligible inside the 2 s budget).
- Port collision risk if 7223 is already taken — surface a clear error and let the user override via settings.

### Neutral
- A future remote-machine consumer would need an auth story; v1 is loopback-only.

## Alternatives considered
- **Named pipe (`\\.\pipe\mockingbird`)** — rejected: shell hooks can't talk to named pipes without a helper binary, and concurrency needs explicit per-instance pipe handling. Reconsider if HTTP overhead ever matters (it won't at ≤2 s budgets).
- **Per-call CLI invocation** — rejected as primary surface (~50–100ms per Windows process spawn). Kept as a thin wrapper over HTTP.
- **gRPC / WebSocket / protobuf** — overkill for `{text, voice}`. Rejected.

## References
- Vision: `.agenthoff/vision.md`
- BC README: `.agenthoff/contexts/main/README.md` (speak endpoint vocabulary)
