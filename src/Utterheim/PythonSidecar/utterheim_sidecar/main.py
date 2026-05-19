"""
Utterheim sidecar wrapper around pocket-tts (ADR 0015).

Adds two HTTP routes onto pocket-tts's existing FastAPI app and re-exports a
typer `serve` command that mirrors `pocket_tts serve` so the C# SidecarHost
can swap one launch arg and otherwise behave identically:

    python -m pocket_tts        serve --host 127.0.0.1 --port 0
    python -m utterheim_sidecar serve --host 127.0.0.1 --port 0

Routes added:

  POST /export-voice
      Form fields:
          voice_wav  (UploadFile, required) — the sample WAV to clone from.
          voice_id   (str, optional) — used in error messages / logging only.
      Response:
          200 application/octet-stream — exported .safetensors bytes
          400 — bad/short/unreadable WAV
          500 — torch/Mimi failure (text body)

  POST /tts-with-state
      Form fields:
          text         (str, required) — text to synthesise.
          voice_state  (UploadFile, required) — a .safetensors voice profile
                       previously exported by /export-voice.
      Response:
          200 audio/wav (StreamingResponse) — same shape as pocket-tts /tts.

Both routes share pocket-tts's resident TTSModel and avoid paying its
~10-30 s cold-load cost.
"""

from __future__ import annotations

import asyncio
import inspect
import logging
import os
import tempfile
import threading
import time
from typing import Dict, List, Optional

import typer
from fastapi import File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, JSONResponse, StreamingResponse
from starlette.background import BackgroundTask
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request

# Per ADR 0015 we deliberately import pocket-tts internals. If Kyutai
# refactors `main.py` in pocket-tts 3.x these imports will fail and we
# surface a clear error message rather than a half-initialised wrapper.
try:
    from pocket_tts.main import web_app  # FastAPI() instance with /, /health, /tts already mounted
except ImportError as exc:  # pragma: no cover - exercised only on incompatible pocket-tts
    raise ImportError(
        "utterheim_sidecar requires pocket-tts 2.x with the documented "
        "module layout (web_app exported from pocket_tts.main). "
        "Update utterheim or pin pocket-tts to a compatible 2.x release."
    ) from exc

# These symbols are imported lazily inside the request handlers so an
# unrelated import-time failure (e.g. missing optional torch backend) does
# not prevent `python -m utterheim_sidecar serve` from at least logging
# the problem.

logger = logging.getLogger("utterheim_sidecar")

app = typer.Typer(add_completion=False)


@app.callback()
def _root() -> None:
    # No-op root callback. Typer collapses single-command apps into a
    # command-less CLI when no callback is registered, which would make
    # `python -m utterheim_sidecar serve ...` fail with "Got unexpected
    # extra argument (serve)". The callback's presence keeps Typer in
    # multi-command mode so the C# SidecarHost's invocation works unchanged.
    return None


# Multi-language model registry (main-039 / ADR 0024). Populated by `serve`
# at startup from the `--language` CLI flags; keys are the lower-case
# pocket-tts language literals (`english`, `german`). The C# host tags each
# speak request with `X-Voice-Language: <key>` and the middleware below
# swaps `pocket_tts.main.tts_model` to the matching entry before the
# request reaches pocket-tts's `/tts` handler (which reads the model as a
# module-level global). Per ADR 0007 the C# speak queue serialises
# requests, so the swap is safe without a per-request lock.
_RESIDENT_MODELS: Dict[str, object] = {}
# First language passed to `serve` — also the back-compat default when a
# request omits the `X-Voice-Language` header (e.g. an older C# host
# before this PR landed, or a curl probe from the dev console).
_DEFAULT_LANGUAGE: Optional[str] = None


def _route_paths_needing_model() -> frozenset:
    """Routes whose handler reads pocket_tts.main.tts_model.

    Two read paths exist:
    - `/tts` and `/tts-with-state` call the generation code that reads the
      module-level model (main-039).
    - `/export-voice` calls `tts_model.get_state_for_audio_prompt(...)` to
      encode the cloning sample. main-041 routes this through the matching
      resident model so a German clone is encoded by the German `TTSModel`,
      keeping the per-language fidelity ADR 0023 promised.

    Other paths (`/health`, `/`) don't need a model swap and are skipped by
    the middleware to keep the hot path narrow.
    """
    return frozenset({"/tts", "/tts-with-state", "/export-voice"})


# ---------------------------------------------------------------------------
# main-045 prototype: opt-in Stop-cancellation propagation into pocket-tts.
#
# Why this code exists
# --------------------
# pocket-tts 2.x has no cancellation surface (see ADR 0026 § Context, ADR 0027
# § Context). When the C# host hits Stop, the outbound HTTP response is closed
# but the sidecar's `_autoregressive_generation` loop keeps running on a daemon
# thread, accruing CPU and tensor allocations (~+100 MB RSS per Stop→Play cycle).
# ADR 0027 enumerates five mechanisms; option (e) — wrapper-level disconnect +
# runtime monkey-patch of `_autoregressive_generation` — is the current
# recommendation.
#
# main-045 (this spike) ships the prototype as an OPT-IN flag so production
# behaviour is unchanged when the flag is off. main-046 will flip the default
# (or delete the flag) once the user's measurement campaign confirms or
# unseats option (e).
#
# Flag and modes
# --------------
# Environment variable `UTTERHEIM_CANCEL_PROTOTYPE`:
#   unset / empty / "off"   — production behaviour (no patching, no disconnect
#                             wrapping). Default.
#   "b"                     — option (b) wrapper-only: wrap the outbound
#                             streaming responses to observe Starlette
#                             disconnect, break the iteration. Does NOT stop
#                             the producer thread (predicted to fail H1).
#   "e"                     — option (e) hybrid: option (b) PLUS monkey-patch
#                             `pocket_tts.models.tts_model.TTSModel._autoregressive_generation`
#                             to read a per-model `threading.Event` and break
#                             the inner `for generation_step in range(...)` loop.
#                             Sentinel-pushes `("done", None)` to result_queue
#                             and `None` to latents_queue on early-return
#                             (main-045 H4) so the consumer in
#                             `_generate_audio_stream_short_text` (tts_model.py:633)
#                             unblocks.
# Any other value is rejected at sidecar startup.
#
# Per-model event
# ---------------
# ADR 0007 serialises speak requests through the C# Channel<T>; pocket-tts is
# called for one request at a time. So one `threading.Event` per resident model
# (stashed as `model._utterheim_stop_event`) is sufficient — no per-request demux.
# The middleware clears the event at the start of each routed request; the
# disconnect handler sets it; the monkey-patched method reads it.
#
# Startup sanity check
# --------------------
# When the flag is "e", `serve` verifies (before binding the port) that:
#   1. `pocket_tts.models.tts_model.TTSModel._autoregressive_generation` exists
#      (the monkey-patch target is reachable).
#   2. Its argument signature still matches the known shape (per main-045
#      refinement: `self`, then the args observed in pocket-tts 2.x at line 744).
#   3. The patched method has actually replaced the original (`is` identity
#      check — guards against the patch silently no-op'ing if a future
#      pocket-tts version moves the method off `TTSModel`).
# Failure: refuse to start with a clear error pointing at this file.
# ---------------------------------------------------------------------------


CANCEL_FLAG_ENV = "UTTERHEIM_CANCEL_PROTOTYPE"
CANCEL_MODES = ("off", "b", "e")
# Disconnect-poll cadence: we wrap StreamingResponse iteration in an async
# generator that calls `await request.is_disconnected()` every CANCEL_POLL_BYTES
# or every yielded chunk (whichever comes first). 200 ms is the target wall
# clock between checks — the ADR 0026 budget of ≤2 s for full recovery gives
# us ~10 polls of headroom.
CANCEL_DISCONNECT_POLL_SECONDS = 0.2


def _read_cancel_mode() -> str:
    """Read and validate the prototype mode from the environment.

    Called once at sidecar startup so the validation error is loud and early.
    Any value outside CANCEL_MODES raises RuntimeError; the caller (`serve`)
    surfaces this as a typer error.
    """
    raw = (os.environ.get(CANCEL_FLAG_ENV) or "").strip().lower()
    if raw in ("", "off"):
        return "off"
    if raw not in CANCEL_MODES:
        raise RuntimeError(
            f"{CANCEL_FLAG_ENV}={raw!r} is not a recognised mode. "
            f"Valid values: {list(CANCEL_MODES)} (or unset / empty for 'off')."
        )
    return raw


def _get_or_create_stop_event(model) -> threading.Event:
    """Return the per-model stop event, lazily creating it on first access.

    Per ADR 0007 the C# queue serialises speak requests, so one event per
    resident model is sufficient. The event lives on the model instance via
    a `_utterheim_stop_event` attribute (underscored to make the
    utterheim-owned coupling visible if a future maintainer greps for it).
    """
    event = getattr(model, "_utterheim_stop_event", None)
    if event is None:
        event = threading.Event()
        model._utterheim_stop_event = event
    return event


def _patch_autoregressive_generation() -> str:
    """Monkey-patch TTSModel._autoregressive_generation with a stop-event
    aware version (ADR 0027 option d, used as part of option e).

    The patched method calls the original in chunks: we cannot rewrite the
    inner loop without copying ~50 lines of private pocket-tts code that
    changes shape between releases. Instead we wrap the original and check
    the per-model stop event around each call — but the original method runs
    its `for generation_step in range(max_gen_len)` loop synchronously and
    only returns at the end, which would defeat the purpose.

    The actual implementation strategy (chosen by main-045 refinement and
    pinned here):

      1. Set a thread-local `_utterheim_stop_event` reference at entry.
      2. Install a `sys.settrace` callback that checks the event each line
         and raises `_UtterheimStopRequested` to break out.
      3. Catch `_UtterheimStopRequested` at the wrapper boundary and push
         the sentinel values onto the model's `latents_queue` and
         `result_queue` (main-045 H4) so the consumer thread unblocks.

    Trace-based interruption is the surgical option: zero invasiveness on
    the pocket-tts source, no need to mirror the inner loop logic. The cost
    is per-line trace overhead while synthesis is running; we measure this
    in the user's empirical pass (it should be invisible — torch ops dwarf
    the trace cost).

    Returns the human-readable description of what was patched, for logging.

    Raises RuntimeError if the target method is missing or has an
    unexpected signature — this is the startup sanity check (main-045 H3).
    """
    from pocket_tts.models import tts_model as _tts_module

    target_class = getattr(_tts_module, "TTSModel", None)
    if target_class is None:
        raise RuntimeError(
            "main-045 cancellation prototype: "
            "pocket_tts.models.tts_model.TTSModel is missing. "
            "pocket-tts API has shifted; update utterheim_sidecar/main.py."
        )

    original = getattr(target_class, "_autoregressive_generation", None)
    if original is None:
        raise RuntimeError(
            "main-045 cancellation prototype: "
            "pocket_tts.models.tts_model.TTSModel._autoregressive_generation is missing. "
            "pocket-tts API has shifted; update utterheim_sidecar/main.py."
        )

    # Inspect signature to fail loud if pocket-tts renamed/reshaped the args.
    # Known signature (pocket-tts 2.0.0, tts_model.py:744 per main-045 refinement):
    #   def _autoregressive_generation(self, ...)
    # We don't enforce arg names beyond `self` because the inner args have
    # rotated between pocket-tts dev versions; the contract we rely on is
    # method-on-TTSModel-takes-self-first. A stricter check here would just
    # produce false positives on every pocket-tts patch release.
    try:
        sig = inspect.signature(original)
    except (TypeError, ValueError) as exc:  # pragma: no cover - defensive
        raise RuntimeError(
            f"main-045 cancellation prototype: could not introspect "
            f"_autoregressive_generation signature: {exc}"
        ) from exc

    params = list(sig.parameters.values())
    if not params or params[0].name != "self":
        raise RuntimeError(
            "main-045 cancellation prototype: "
            "_autoregressive_generation first parameter is not 'self' "
            f"(got {[p.name for p in params]}). "
            "pocket-tts may have promoted the method to a free function; "
            "update utterheim_sidecar/main.py."
        )

    class _UtterheimStopRequested(Exception):
        """Raised by the trace callback to break out of the synchronous
        autoregressive loop. Caught at the patched-method boundary.
        Not exposed to pocket-tts code — purely an internal flow-control
        token for the prototype."""

    # Trace callback factory: closes over the stop event so each invocation
    # of the patched method gets its own check. sys.settrace's callback
    # signature is `(frame, event, arg) -> callable | None`; we return
    # ourselves for `call` events to install line-level checking on the
    # frame, and check the event on every `line` event.
    def _make_trace(stop_event: threading.Event):
        def _trace(frame, event, arg):
            if event == "line" and stop_event.is_set():
                raise _UtterheimStopRequested()
            return _trace

        return _trace

    def patched(self, *args, **kwargs):
        stop_event = _get_or_create_stop_event(self)
        # main-045 H4 (corrected 2026-05-19 during prototype measurement):
        # pocket-tts does NOT store latents_queue / result_queue as attrs on
        # self — they are locals in `_generate_audio_stream_short_text`
        # (tts_model.py:647-648), passed positionally as the 4th arg to
        # `_autoregressive_generation`. Pull it from our call args. Pushing
        # None to latents_queue unblocks the decoder thread
        # (`_decode_audio_worker` at tts_model.py:436), which itself pushes
        # ("done", None) to result_queue (tts_model.py:470), which unblocks
        # the consumer in `_generate_audio_stream_short_text`. The whole
        # chain unwinds without further intervention. main-046 will replace
        # this trace-based approach with direct method-body replacement to
        # also eliminate the residual ~10-20 MB/cycle drift from trace-frame
        # retention.
        latents_queue = kwargs.get("latents_queue")
        if latents_queue is None and len(args) >= 4:
            latents_queue = args[3]

        def _push_latents_sentinel():
            if latents_queue is None:
                return
            try:
                latents_queue.put(None)
            except Exception:  # pragma: no cover - best-effort sentinel
                logger.exception(
                    "main-045: latents_queue sentinel push failed"
                )

        # Fast path: no Stop in flight. Run unmodified — trace overhead is
        # zero when settrace is never called.
        if not stop_event.is_set():
            # Arm a trace that raises if the event fires mid-loop.
            import sys as _sys
            import gc as _gc

            _trace = _make_trace(stop_event)
            previous_trace = _sys.gettrace()
            _sys.settrace(_trace)
            try:
                return original(self, *args, **kwargs)
            except _UtterheimStopRequested:
                _push_latents_sentinel()
                # Force GC so the tracebacks (which capture frame locals
                # including the KV cache tensors) get released promptly
                # rather than waiting on the generational collector.
                _gc.collect()
                return None
            finally:
                _sys.settrace(previous_trace)
        else:
            # Event was already set before we entered — treat as immediate
            # cancellation. Caller doesn't need a result.
            _push_latents_sentinel()
            return None

    # Preserve the function metadata so introspection-driven code (e.g.
    # tracebacks, debuggers) still names the original.
    patched.__name__ = getattr(original, "__name__", "_autoregressive_generation")
    patched.__qualname__ = getattr(
        original, "__qualname__", "TTSModel._autoregressive_generation"
    )
    patched.__doc__ = (original.__doc__ or "") + (
        "\n\n[utterheim_sidecar main-045 prototype: patched to observe "
        f"per-model {CANCEL_FLAG_ENV}=e stop event via sys.settrace]"
    )
    # Stash the original so we can verify the patch identity at startup
    # and revert in the unlikely case the sidecar is run with the flag
    # toggled in-process (we don't currently support that, but the attr
    # is harmless).
    patched._utterheim_original = original  # type: ignore[attr-defined]

    target_class._autoregressive_generation = patched

    # Sanity-check: confirm the patch actually replaced the attribute.
    if getattr(target_class, "_autoregressive_generation", None) is not patched:
        raise RuntimeError(
            "main-045 cancellation prototype: monkey-patch assignment did "
            "not stick. TTSModel may use slots or a descriptor that "
            "rejects rebinding. Investigate before main-046 ships."
        )

    return (
        "patched pocket_tts.models.tts_model.TTSModel._autoregressive_generation "
        "with trace-based stop-event observation (main-045 H2/H3/H4 prototype)"
    )


def _push_stop_sentinels(model) -> None:
    """Push the values that the consumer in `_generate_audio_stream_short_text`
    (tts_model.py:633) blocks on, so it unwinds cleanly after we short-circuit
    the producer (main-045 H4).

    pocket-tts 2.x's consumer reads `result_queue.get()` in a blocking loop
    waiting for a `("done", None)` tuple OR a `("chunk", data)` tuple. It also
    reads `latents_queue.get()` in a separate consumer waiting for `None` as
    a poison pill. Without these sentinels the producer's early return leaks
    the consumer thread (which then keeps a reference to the model and its
    tensors — exactly the leak we are trying to fix).

    The queues live as instance attrs on the model during a single generation
    call (`self.result_queue` / `self.latents_queue` in tts_model.py:633
    per main-045 refinement). They may not exist if the model was created
    but no generation ever ran — guard with getattr.
    """
    result_q = getattr(model, "result_queue", None)
    latents_q = getattr(model, "latents_queue", None)

    if result_q is not None:
        try:
            result_q.put(("done", None))
        except Exception:  # pragma: no cover - best-effort sentinel
            logger.exception("main-045: result_queue sentinel push failed")

    if latents_q is not None:
        try:
            latents_q.put(None)
        except Exception:  # pragma: no cover - best-effort sentinel
            logger.exception("main-045: latents_queue sentinel push failed")


async def _disconnect_aware_iterator(request: Request, source, model):
    """Wrap a synchronous WAV-byte generator with a disconnect poll.

    `source` is the underlying generator returned by
    `pocket_tts.main.generate_data_with_state(...)`. It yields bytes (or
    bytes-like chunks) synchronously, driven by an internal producer thread.

    We iterate it from a threadpool worker (StreamingResponse already runs
    sync iterators on a thread), polling `await request.is_disconnected()`
    between yields. On disconnect:
      - mode "b": just stop iterating. The producer thread keeps running
        (predicted H1 falsifier — option (b) alone is insufficient).
      - mode "e": set the per-model stop event so the monkey-patched
        `_autoregressive_generation` breaks out within one inference step.

    The wrapper closes the source generator (if it exposes `.close()`) on
    exit so the pocket-tts cleanup path runs.
    """
    cancel_mode = (os.environ.get(CANCEL_FLAG_ENV) or "off").strip().lower()
    stop_event = _get_or_create_stop_event(model) if cancel_mode == "e" else None
    # Make sure we don't inherit a previously-set event from a prior request.
    if stop_event is not None:
        stop_event.clear()

    # `source` may be a plain generator or an iterable. Normalise to iterator.
    iterator = iter(source)
    last_poll = time.monotonic()

    try:
        while True:
            # Pull the next chunk on a thread so the event loop stays responsive
            # and our disconnect poll can fire even if the producer thread is
            # slow (e.g. waiting on the autoregressive loop).
            try:
                chunk = await asyncio.to_thread(next, iterator)
            except StopIteration:
                break

            yield chunk

            # Disconnect poll — every CANCEL_DISCONNECT_POLL_SECONDS, ask
            # Starlette whether the client TCP is still attached.
            now = time.monotonic()
            if now - last_poll >= CANCEL_DISCONNECT_POLL_SECONDS:
                last_poll = now
                try:
                    disconnected = await request.is_disconnected()
                except Exception:  # pragma: no cover - defensive
                    disconnected = False
                if disconnected:
                    logger.info(
                        "main-045 prototype (mode=%s): client disconnected "
                        "mid-stream; signalling cancellation.",
                        cancel_mode,
                    )
                    if stop_event is not None:
                        stop_event.set()
                    break
        # main-045 fix (2026-05-19 during prototype measurement):
        # unconditionally set the stop_event in finally — whether we got here
        # via disconnect-poll OR via Starlette cancelling our to_thread
        # chunk-pull. The cancellation path was the bug: CancelledError fires
        # before the disconnect-poll has a chance to run, so without this
        # line the stop_event was never set and the producer thread ran to
        # completion. Also: do NOT call source.close() — when
        # to_thread(next, iterator) is mid-execution on a worker thread,
        # close() from this thread raises "generator already executing". The
        # patched _autoregressive_generation sees stop_event.is_set() on its
        # next line trace, breaks the loop, pushes the H4 sentinels, the
        # producer thread exits, the consumer drains, the source generator
        # unwinds naturally.
    finally:
        if stop_event is not None:
            stop_event.set()
            # Do NOT clear() here — the patched generation loop may still be
            # observing it on another thread. The next request's wrapper
            # entry clears the event before kicking off synthesis.


class LanguageRoutingMiddleware(BaseHTTPMiddleware):
    """Read the C# host's `X-Voice-Language` header and swap
    `pocket_tts.main.tts_model` to the matching resident model before the
    pocket-tts `/tts` handler (or our `/tts-with-state` handler) runs.

    If the header is absent we fall back to `_DEFAULT_LANGUAGE` (the first
    `--language` passed to `serve`) so the pre-main-039 wire shape (no
    header) still works. If the header names a language no resident model
    was preloaded for we return a structured 503 JSON envelope that names
    the missing language — never a process crash (AC 5 of main-039).
    """

    async def dispatch(self, request: Request, call_next):
        if request.url.path not in _route_paths_needing_model() or not _RESIDENT_MODELS:
            return await call_next(request)

        wanted = request.headers.get("x-voice-language") or _DEFAULT_LANGUAGE
        if wanted is None:
            # _RESIDENT_MODELS is non-empty (checked above) but _DEFAULT_LANGUAGE
            # never set — happens only if `serve` was called without going
            # through our entry point. Treat as a misconfigured sidecar.
            return JSONResponse(
                status_code=503,
                content={
                    "error": "language_unset",
                    "detail": (
                        "No X-Voice-Language header and no default language "
                        "configured on this sidecar."
                    ),
                },
            )

        key = wanted.strip().lower()
        model = _RESIDENT_MODELS.get(key)
        if model is None:
            return JSONResponse(
                status_code=503,
                content={
                    "error": "language_not_preloaded",
                    "language": key,
                    "available": sorted(_RESIDENT_MODELS.keys()),
                    "detail": (
                        f"No resident TTSModel for language '{key}'. "
                        f"Sidecar preloaded: {sorted(_RESIDENT_MODELS.keys())}. "
                        "Restart the sidecar with that language in --language."
                    ),
                },
            )

        # Swap the module global pocket-tts's /tts handler reads (and that
        # _get_resident_tts_model below relays for /tts-with-state). Safe
        # because C# host serialises speak requests per ADR 0007.
        from pocket_tts import main as pocket_main

        pocket_main.tts_model = model

        # main-045 prototype: when the cancellation flag is on and the route
        # is the streaming pocket-tts /tts handler (the one we don't own —
        # /tts-with-state already wraps itself at the handler layer), wrap
        # the response body iterator with our disconnect-aware iterator so
        # the prototype covers both code paths.
        cancel_mode = (os.environ.get(CANCEL_FLAG_ENV) or "off").strip().lower()
        if cancel_mode in ("b", "e") and request.url.path == "/tts":
            # Clear the event for mode "e" before the inner handler kicks off
            # synthesis. (The middleware runs before the handler that drives
            # the producer thread, so this is the right hook point.)
            if cancel_mode == "e":
                _get_or_create_stop_event(model).clear()

            response = await call_next(request)
            # StreamingResponse has a `body_iterator` (async-iterator over
            # bytes). Wrap it. pocket-tts's /tts handler returns a
            # StreamingResponse whose body_iterator iterates a generator —
            # we adapt to our shared helper which expects a sync iterable
            # because pocket-tts yields synchronously.
            body = getattr(response, "body_iterator", None)
            if body is not None:
                async def _wrapped_async_body():
                    # response.body_iterator is async; iterate it on the event
                    # loop and pump bytes through the disconnect poll.
                    last_poll = time.monotonic()
                    stop_event = (
                        _get_or_create_stop_event(model) if cancel_mode == "e" else None
                    )
                    try:
                        async for chunk in body:
                            yield chunk
                            now = time.monotonic()
                            if now - last_poll >= CANCEL_DISCONNECT_POLL_SECONDS:
                                last_poll = now
                                try:
                                    disconnected = await request.is_disconnected()
                                except Exception:  # pragma: no cover - defensive
                                    disconnected = False
                                if disconnected:
                                    logger.info(
                                        "main-045 prototype (mode=%s): /tts "
                                        "client disconnected mid-stream; "
                                        "signalling cancellation.",
                                        cancel_mode,
                                    )
                                    if stop_event is not None:
                                        stop_event.set()
                                    break
                    finally:
                        # Try to close the inner async iterator if it supports it.
                        aclose = getattr(body, "aclose", None)
                        if callable(aclose):
                            try:
                                await aclose()
                            except Exception:  # pragma: no cover - best-effort
                                logger.exception(
                                    "main-045 prototype: /tts body aclose() raised"
                                )
                        if stop_event is not None:
                            stop_event.clear()

                response.body_iterator = _wrapped_async_body()
            return response

        return await call_next(request)


# Register the middleware once at module import time so it's wired before
# uvicorn.run binds the app. starlette refuses add_middleware on a running
# app, hence the import-time hook rather than inside `serve`.
web_app.add_middleware(LanguageRoutingMiddleware)


def _get_resident_tts_model():
    """Return the pocket-tts resident TTSModel that the language-routing
    middleware just swapped in (or the single model loaded by `serve` in
    the pre-main-039 single-language path)."""
    from pocket_tts import main as pocket_main

    model = getattr(pocket_main, "tts_model", None)
    if model is None:
        raise HTTPException(
            status_code=503,
            detail=(
                "pocket-tts model is not loaded yet. The serve command must "
                "complete model initialisation before requests are accepted."
            ),
        )
    return model


def _import_state_helpers():
    """Pull export_model_state / import_model_state out of pocket-tts.

    pocket-tts 2.x re-exports `export_model_state` from the top-level
    package (see its `__all__`) but ships the reverse helper only as the
    private `_import_model_state` inside `pocket_tts.models.tts_model`.
    The kyutai-tts-2026-05-01 research note's snippet (`from pocket_tts
    import ... import_model_state`) does not match the released package.
    Try the public top-level name first so a future pocket-tts release
    that promotes the helper "just works"; fall back to the private name
    on the currently-shipping versions.
    """
    from pocket_tts import export_model_state

    try:
        from pocket_tts import import_model_state  # type: ignore[attr-defined]
    except ImportError:
        from pocket_tts.models.tts_model import (
            _import_model_state as import_model_state,
        )

    return export_model_state, import_model_state


# ---------------------------------------------------------------------------
# /export-voice
# ---------------------------------------------------------------------------


@web_app.post("/export-voice")
async def export_voice(
    voice_wav: UploadFile = File(...),
    voice_id: Optional[str] = Form(None),
):
    """Clone a voice: WAV upload in, .safetensors bytes out."""
    label = voice_id or voice_wav.filename or "<unnamed>"

    # Stage the upload to a temp file because torchaudio (used by
    # get_state_for_audio_prompt) reads from a path, not a stream.
    tmp_dir = tempfile.mkdtemp(prefix="utterheim_clone_")
    wav_path = os.path.join(tmp_dir, "sample.wav")
    out_path = os.path.join(tmp_dir, "voice.safetensors")

    try:
        # Write upload bytes to disk. UploadFile.read() loads the full file
        # into memory; clones are <= ~30 s of audio so this is fine.
        contents = await voice_wav.read()
        if not contents:
            raise HTTPException(status_code=400, detail="voice_wav is empty.")
        with open(wav_path, "wb") as fh:
            fh.write(contents)

        tts_model = _get_resident_tts_model()
        export_model_state, _ = _import_state_helpers()

        try:
            state = tts_model.get_state_for_audio_prompt(wav_path, truncate=True)
        except Exception as exc:
            logger.exception(
                "get_state_for_audio_prompt failed for voice '%s'", label
            )
            # Treat audio-encoder failures as 400 (caller's audio is bad)
            # not 500 (engine bug) — the torch traceback is the telltale.
            raise HTTPException(
                status_code=400,
                detail=f"Could not encode audio prompt for '{label}': {exc}",
            ) from exc

        try:
            export_model_state(state, out_path)
        except Exception as exc:
            logger.exception("export_model_state failed for voice '%s'", label)
            raise HTTPException(
                status_code=500,
                detail=f"Could not export voice state for '{label}': {exc}",
            ) from exc

        # FileResponse streams the file then deletes the entire temp dir
        # via the BackgroundTask. We must NOT delete tmp_dir before
        # FileResponse has finished sending the bytes.
        return FileResponse(
            path=out_path,
            media_type="application/octet-stream",
            filename=f"{voice_id or 'voice'}.safetensors",
            background=BackgroundTask(_cleanup_tmp_dir, tmp_dir),
        )
    except HTTPException:
        _cleanup_tmp_dir(tmp_dir)
        raise
    except Exception as exc:
        _cleanup_tmp_dir(tmp_dir)
        logger.exception("Unhandled error in /export-voice for voice '%s'", label)
        raise HTTPException(status_code=500, detail=str(exc)) from exc


def _cleanup_tmp_dir(path: str) -> None:
    import shutil

    try:
        shutil.rmtree(path, ignore_errors=True)
    except Exception:  # pragma: no cover - best-effort cleanup
        logger.warning("Failed to clean up temp dir %s", path, exc_info=True)


# ---------------------------------------------------------------------------
# /tts-with-state
# ---------------------------------------------------------------------------


@web_app.post("/tts-with-state")
async def tts_with_state(
    request: Request,
    text: str = Form(...),
    voice_state: UploadFile = File(...),
):
    """Synthesise `text` using the voice profile uploaded as `voice_state`."""
    if not text.strip():
        raise HTTPException(status_code=400, detail="text is required.")

    tmp_dir = tempfile.mkdtemp(prefix="utterheim_speak_")
    state_path = os.path.join(tmp_dir, "voice.safetensors")

    try:
        contents = await voice_state.read()
        if not contents:
            raise HTTPException(status_code=400, detail="voice_state is empty.")
        with open(state_path, "wb") as fh:
            fh.write(contents)

        tts_model = _get_resident_tts_model()
        _, import_model_state = _import_state_helpers()

        # pocket-tts 2.x's private `_import_model_state(source, device)` requires
        # the resident model's torch device as a positional arg (see
        # pocket_tts/models/tts_model.py: `_import_model_state(source, self.device)`).
        # If a future pocket-tts release promotes a public `import_model_state`
        # with a different signature, fall back to the single-arg form.
        try:
            try:
                state = import_model_state(state_path, tts_model.device)
            except TypeError:
                state = import_model_state(state_path)
        except Exception as exc:
            logger.exception("import_model_state failed")
            raise HTTPException(
                status_code=400,
                detail=f"Could not import voice_state: {exc}",
            ) from exc

        # Reuse the same streaming code path /tts uses. pocket_tts.main exposes
        # `generate_data_with_state` (per ADR 0015 / pocket-tts 2.x source);
        # if a future pocket-tts version renames it we'd patch this single line.
        from pocket_tts import main as pocket_main

        generator = getattr(pocket_main, "generate_data_with_state", None)
        if generator is None:
            raise HTTPException(
                status_code=500,
                detail=(
                    "pocket_tts.main.generate_data_with_state is not "
                    "available in this pocket-tts release."
                ),
            )

        # generate_data_with_state(text, model_state) yields WAV-framed bytes.
        # Stream them with the same media type pocket-tts /tts uses.
        cancel_mode = (os.environ.get(CANCEL_FLAG_ENV) or "off").strip().lower()
        underlying = generator(text, state)
        if cancel_mode in ("b", "e"):
            # Wrap with the disconnect-aware iterator (main-045 prototype).
            # The wrapper polls request.is_disconnected() between yields and,
            # in mode "e", sets the per-model stop event so the monkey-patched
            # `_autoregressive_generation` breaks out of its inner loop.
            stream = _disconnect_aware_iterator(request, underlying, tts_model)
        else:
            stream = underlying
        return StreamingResponse(
            stream,
            media_type="audio/wav",
            background=BackgroundTask(_cleanup_tmp_dir, tmp_dir),
        )
    except HTTPException:
        _cleanup_tmp_dir(tmp_dir)
        raise
    except Exception as exc:
        _cleanup_tmp_dir(tmp_dir)
        logger.exception("Unhandled error in /tts-with-state")
        raise HTTPException(status_code=500, detail=str(exc)) from exc


# ---------------------------------------------------------------------------
# `serve` typer command — mirrors pocket_tts.main.serve exactly so the C#
# SidecarHost can swap "pocket_tts" for "utterheim_sidecar" with no other
# changes to its arg list.
# ---------------------------------------------------------------------------


@app.command()
def serve(
    host: str = typer.Option("127.0.0.1", help="Bind address."),
    port: int = typer.Option(0, help="Port (0 = OS-assigned)."),
    language: List[str] = typer.Option(
        None,
        "--language",
        help=(
            "Language(s) to preload as resident TTSModel instances. Repeatable "
            "(e.g. `--language english --language german`); the first value is "
            "the default for requests that don't carry an X-Voice-Language "
            "header. Omit the flag to preload only the pocket-tts default "
            "language (english), matching the pre-main-039 single-model "
            "behaviour. Per ADR 0024 the v1 production set is english + german."
        ),
    ),
    config: Optional[str] = typer.Option(None, help="Override the model config name."),
    quantize: bool = typer.Option(False, help="Use the int8-quantised model."),
):
    """Start the utterheim-wrapped pocket-tts FastAPI app.

    Loads one `TTSModel` per language in `--language` (main-039) and stashes
    them in `_RESIDENT_MODELS` keyed by language name. The middleware
    swaps `pocket_tts.main.tts_model` per request based on the C# host's
    `X-Voice-Language` header. The first language passed is the default
    for requests that omit the header (back-compat with the pre-main-039
    single-language wire shape).
    """
    from pocket_tts import TTSModel
    from pocket_tts import main as pocket_main

    # main-045 prototype gate: validate the cancellation-flag env var *first*
    # so a typo doesn't slip past us as a silent "off". If mode is "e", apply
    # the monkey-patch and run the sanity check before we load any models —
    # an early patch failure is easier to triage than a mid-load crash.
    cancel_mode = _read_cancel_mode()
    if cancel_mode == "off":
        logger.info(
            "utterheim_sidecar: cancellation prototype is OFF "
            "(set %s=b or %s=e to enable main-045 measurement modes).",
            CANCEL_FLAG_ENV,
            CANCEL_FLAG_ENV,
        )
    elif cancel_mode == "b":
        logger.info(
            "utterheim_sidecar: cancellation prototype mode=b (wrapper-only). "
            "Outbound streams will observe client disconnect; producer thread "
            "is NOT interrupted (option (b) — predicted H1 falsifier)."
        )
    elif cancel_mode == "e":
        logger.info(
            "utterheim_sidecar: cancellation prototype mode=e (hybrid). "
            "Wrapper + monkey-patch of "
            "pocket_tts.models.tts_model.TTSModel._autoregressive_generation."
        )
        patch_desc = _patch_autoregressive_generation()
        logger.info("utterheim_sidecar: main-045 sanity check passed — %s", patch_desc)

    # Normalise the language list. `typer.Option(None, ...)` with a List[str]
    # type passes `None` (not `[]`) when the flag is omitted entirely.
    # Default to ["english"] in that case so the bare `serve` invocation
    # keeps loading the english model the way it always did.
    languages: List[str] = []
    if language:
        for raw in language:
            if raw is None:
                continue
            key = raw.strip().lower()
            if key and key not in languages:
                languages.append(key)
    if not languages:
        languages = ["english"]

    # `config` is incompatible with multi-language preload (it points at a
    # single .yaml). Reject the combination loudly rather than silently load
    # the same config N times.
    if config is not None and len(languages) > 1:
        raise typer.BadParameter(
            "--config is incompatible with multiple --language values; "
            "the config arg names a single model lineage."
        )

    global _DEFAULT_LANGUAGE
    _DEFAULT_LANGUAGE = languages[0]
    _RESIDENT_MODELS.clear()

    total_start = time.monotonic()
    for lang in languages:
        load_kwargs = {"language": lang}
        if config is not None:
            load_kwargs["config"] = config
        if quantize:
            load_kwargs["quantize"] = True

        logger.info("utterheim_sidecar: loading pocket-tts model %s", load_kwargs)
        per_start = time.monotonic()
        # `language=` is contractual since pocket-tts 2.0.0 (see PythonRuntimeBootstrapper
        # pin `pocket-tts>=2.0.0,<3`). The redundant TypeError fallback that
        # used to wrap this call was removed in main-043 once the pin made
        # the kwarg unconditionally available.
        model = TTSModel.load_model(**load_kwargs)
        per_elapsed = time.monotonic() - per_start
        _RESIDENT_MODELS[lang] = model
        logger.info(
            "utterheim_sidecar: loaded language '%s' in %.2fs", lang, per_elapsed
        )

    total_elapsed = time.monotonic() - total_start
    logger.info(
        "utterheim_sidecar: %d resident model(s) ready in %.2fs (languages=%s, default='%s')",
        len(_RESIDENT_MODELS),
        total_elapsed,
        sorted(_RESIDENT_MODELS.keys()),
        _DEFAULT_LANGUAGE,
    )

    # Seed pocket_tts.main.tts_model with the default model so the very first
    # request — before the middleware has run — still finds a model. Every
    # subsequent request will be swapped by the middleware to match its
    # X-Voice-Language header.
    pocket_main.tts_model = _RESIDENT_MODELS[_DEFAULT_LANGUAGE]

    import uvicorn

    # Bind to the same FastAPI app the wrapper has now decorated with the
    # /export-voice and /tts-with-state routes and the language-routing
    # middleware. Uvicorn's startup banner
    # ("Uvicorn running on http://127.0.0.1:NNNN") matches the SidecarHost
    # PortRegex unchanged.
    uvicorn.run(web_app, host=host, port=port, log_level="info")


if __name__ == "__main__":  # pragma: no cover - module entry point
    app()
