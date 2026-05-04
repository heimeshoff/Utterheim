---
id: 0013
title: HttpClient calls into the pocket-tts sidecar must use ResponseHeadersRead
scope: bounded-context
context: main
status: accepted
date: 2026-05-04
supersedes: []
superseded_by: []
related_tasks: [main-023, main-024]
related_research: []
---

# ADR 0013: HttpClient calls into the pocket-tts sidecar must use ResponseHeadersRead

## Context

The pocket-tts sidecar (ADR 0002) returns synthesised audio as a FastAPI
`StreamingResponse` with `Transfer-Encoding: chunked`. Bytes are produced
on a generator that yields PCM frames as the model emits them. This is
what makes the vision's ≤2 s first-chunk-latency target reachable: first
audio is intrinsically ~200 ms regardless of total input length.

`System.Net.Http.HttpClient` has two completion modes for response handling:

- `HttpCompletionOption.ResponseContentRead` (the **default**) — the
  awaited `SendAsync` / `PostAsync` does not return until the full
  response body has been read into memory. This silently throws away
  any streaming property of the response; the caller sees only a
  `MemoryStream`-like buffered body once everything has arrived.
- `HttpCompletionOption.ResponseHeadersRead` — `SendAsync` returns as
  soon as the response headers arrive. `Content.ReadAsStreamAsync()`
  then returns a network-backed stream the caller drains live.

main-023 diagnosed this exact mismatch as the sole cause of our
first-chunk latency missing budget by ~10× on medium input and ~70× on
long input. With the default completion option, end-to-end latency
scaled linearly with input size at ~20 ms/char — the signature of
buffering. main-024 confirmed by switching to `ResponseHeadersRead`:
first-audio collapsed to ~190 ms across short, medium, and long
inputs, with no per-character growth term.

## Decision

Any C# call into the pocket-tts sidecar that consumes a streaming
endpoint **must** use `_http.SendAsync(request,
HttpCompletionOption.ResponseHeadersRead, ct)`, not the convenience
`PostAsync`/`GetAsync` overloads.

Concretely, in `PocketTtsEngine.StreamAsync`:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
using var response = await _http.SendAsync(
    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
```

The downstream chunk-reader (`ReadAsStreamAsync()` + 8 KB read loop) is
already streaming-shaped; it just needs a network-backed stream
underneath, which only `ResponseHeadersRead` provides.

The convenience `PostAsync(url, content, ct)` overload is **banned**
for the `/tts` endpoint and should not be reintroduced. A code comment
at the call site explains why so a future "simplify this" refactor
doesn't quietly resurrect the bug.

## Consequences

### Positive
- First-chunk latency becomes input-length-independent at ~190 ms warm,
  ~320 ms cold — well inside the vision's ≤2 s budget across all input
  sizes the user will realistically hit.
- The fix is two lines and required no changes to the chunk-reader,
  the WAV-header strip, or `AudioPlayer`. The streaming pipeline below
  the HTTP layer was already correct; only the transport layer was
  buffering.
- The measurement harness (`examples/perf/measure-latency.ps1`) plus
  the `FIRST-AUDIO-DISPATCH` log line in `AudioPlayer.cs` form a
  permanent regression check — running it against any future change
  to `PocketTtsEngine` will catch a re-buffering regression in seconds.

### Negative
- `ResponseHeadersRead` requires an explicit `HttpRequestMessage`
  (the `*Async(url, content, ct)` convenience overloads don't take a
  completion option). Two lines instead of one at the call site.
- Caller is now responsible for disposing the response body stream
  (`await using var stream = ...`). Already true in our code; flagged
  here so future code that copies this pattern doesn't drop disposal.

### Trade-offs accepted
- We do not generalise this into a custom `HttpClient` factory or
  delegating handler that forces the option for every call. Only one
  call site consumes a streaming sidecar endpoint today; over-engineering
  a global default would be premature. If a second streaming sidecar
  endpoint appears, revisit.

## Alternatives considered

- **Switch transport away from HTTP** (e.g. named pipes, gRPC streaming).
  Rejected: ADR 0003 picked HTTP for good reasons (Kestrel ergonomics,
  curl-able from Claude hooks, trivially concurrent). The latency
  problem was a misuse of the existing transport, not a transport
  defect.
- **Pre-tokenise / pre-split text in the C# host before sending to
  pocket-tts.** Rejected: pocket-tts already does optimal in-engine
  pipelining within a sentence (`tts_model.py:493-502`); there's no
  improvement to be had above the sidecar. main-023 proved the engine
  itself is fast (~63 ms TTFB on 1.2 KB input).
- **Force chunked transfer on the C# side via `TransferEncodingChunked`.**
  N/A — that header is for *outgoing* requests with `HttpClient` as
  client; the sidecar already sends chunked responses correctly. The
  bug was on the consume side.

## Notes

- Reference: main-023 Outcome (diagnosis), main-024 Outcome (fix +
  before/after timings), ADR 0002 (sidecar choice), ADR 0003 (HTTP
  transport), vision §"What success looks like" criterion 1.
- Domain rule recorded in `contexts/main/README.md` glossary entry for
  **First-chunk latency**: warm ~190 ms, cold ~320 ms (alba), measured
  end-to-end via `FIRST-AUDIO-DISPATCH` log line.
