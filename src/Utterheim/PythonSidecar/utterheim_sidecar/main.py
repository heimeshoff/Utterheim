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

Stop-cancellation propagation (main-046, ADR 0027 option (e))
-------------------------------------------------------------
Every `serve` startup patches `pocket_tts.models.tts_model.TTSModel._autoregressive_generation`
with a stop-event-aware reimplementation. The patch inlines the for-loop
body from pocket-tts 2.x's `tts_model.py:744-779` and inserts an explicit
`if stop_event.is_set(): break` between the per-step inference call and
the latent push. Both `/tts` (wrapped at the middleware layer) and
`/tts-with-state` (wrapped at the handler layer) poll
`request.is_disconnected()` every ~200 ms; on disconnect the per-model
`threading.Event` is set, the patched method breaks out within one
inference step, pushes `None` to the `latents_queue` positional arg to
unblock the decoder thread, and returns. ADR 0026 budget: CPU <5 % within
≤2 s of Stop.
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
# Stop cancellation (always on, main-046, ADR 0027 option (e))
#
# pocket-tts 2.x has no first-class cancellation surface. main-045 prototyped
# five options; option (e) — wrapper-level disconnect poll + monkey-patch of
# `TTSModel._autoregressive_generation` — was accepted. main-046 ships the
# production form: direct method-body replacement (no `sys.settrace`).
#
# Per-model event
# ---------------
# ADR 0007 serialises speak requests through the C# Channel<T>; pocket-tts is
# called for one request at a time. So one `threading.Event` per resident
# model (stashed as `model._utterheim_stop_event`) is sufficient — no
# per-request demux. The middleware (or handler) clears the event at the
# start of each routed request; the disconnect handler sets it; the patched
# method reads it.
#
# Startup sanity check (ADR 0027 § Startup sanity check)
# ------------------------------------------------------
# `serve` invokes `_patch_autoregressive_generation()` before any model load.
# That function:
#   1. Imports `pocket_tts.models.tts_model.TTSModel`. Missing → RuntimeError.
#   2. Looks up `TTSModel._autoregressive_generation`. Missing → RuntimeError.
#   3. Calls `inspect.signature(original)`. First param not `self` → RuntimeError
#      (catches free-function promotion).
#   4. Replaces the attribute and asserts `TTSModel._autoregressive_generation is patched`
#      (catches __slots__ / descriptor rebind rejection).
# Any failure refuses sidecar startup with a clear message pointing here.
# ---------------------------------------------------------------------------

# Disconnect-poll cadence: wrapper iterators check `request.is_disconnected()`
# every CANCEL_DISCONNECT_POLL_SECONDS. ADR 0026's ≤2 s budget gives us ~10
# polls of headroom over 200 ms.
CANCEL_DISCONNECT_POLL_SECONDS = 0.2


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
    """Replace `TTSModel._autoregressive_generation` with a stop-event-aware
    reimplementation that inlines the for-loop body from pocket-tts 2.x
    (`tts_model.py:744-779`) with an explicit `if stop_event.is_set(): break`
    between the per-step inference call and the latent push.

    Why direct method-body replacement (and not `sys.settrace`):
    main-045's prototype used a `sys.settrace` line-callback to raise inside
    the loop. CPU recovery worked but the trace machinery retained ~10-20 MB
    of frame state per cancelled cycle. ADR 0027 (accepted 2026-05-19) selects
    direct replacement: ~30 lines of pocket-tts-coupled code, zero trace
    overhead, zero frame retention.

    The replacement body is intentionally small — just the for-loop. It
    relies on the surrounding `_generate_audio_stream_short_text` /
    `_decode_audio_worker` machinery (in pocket-tts) being unchanged. A
    signature-shape drift is caught loud by the startup sanity check below;
    a behaviour drift (upstream changes per-step logic) is silent and
    accepted as a v1 risk.

    Returns a human-readable description of what was patched, for logging.

    Raises RuntimeError if the target class or method is missing, or if its
    signature has shifted in a way that would break the patch.
    """
    from pocket_tts.models import tts_model as _tts_module

    target_class = getattr(_tts_module, "TTSModel", None)
    if target_class is None:
        raise RuntimeError(
            "main-046 cancellation patch: "
            "pocket_tts.models.tts_model.TTSModel is missing. "
            "pocket-tts API has shifted; update utterheim_sidecar/main.py."
        )

    original = getattr(target_class, "_autoregressive_generation", None)
    if original is None:
        raise RuntimeError(
            "main-046 cancellation patch: "
            "pocket_tts.models.tts_model.TTSModel._autoregressive_generation "
            "is missing. pocket-tts API has shifted; update utterheim_sidecar/main.py."
        )

    # Inspect signature to fail loud if pocket-tts renamed/reshaped the args.
    # Known signature (pocket-tts 2.x, tts_model.py:744):
    #   def _autoregressive_generation(
    #       self, model_state: dict, max_gen_len: int,
    #       frames_after_eos: int, latents_queue: queue.Queue,
    #   )
    # We enforce only that the first parameter is `self`. Stricter arg-name
    # checks would produce false positives on every pocket-tts patch release.
    try:
        sig = inspect.signature(original)
    except (TypeError, ValueError) as exc:  # pragma: no cover - defensive
        raise RuntimeError(
            f"main-046 cancellation patch: could not introspect "
            f"_autoregressive_generation signature: {exc}"
        ) from exc

    params = list(sig.parameters.values())
    if not params or params[0].name != "self":
        raise RuntimeError(
            "main-046 cancellation patch: "
            "_autoregressive_generation first parameter is not 'self' "
            f"(got {[p.name for p in params]}). "
            "pocket-tts may have promoted the method to a free function; "
            "update utterheim_sidecar/main.py."
        )

    # Lazy imports — keep the patch function importable when torch isn't
    # installed (e.g. CI on a slim runtime). The patched body itself needs
    # torch, but the import happens at first *call*, not at patch time.
    def patched(self, model_state, max_gen_len, frames_after_eos, latents_queue):
        """Stop-event-aware reimplementation of TTSModel._autoregressive_generation.

        Mirrors pocket-tts 2.x `tts_model.py:744-779` with one added line —
        the `if stop_event.is_set(): break` check between
        `_run_flow_lm_and_increment_step` and `latents_queue.put`. On a stop
        signal we push the `None` sentinel to `latents_queue` (the positional
        arg, NOT a `self.` attr — see main-045 H4 fix) so the decoder thread
        in `_decode_audio_worker` unblocks and unwinds cleanly.
        """
        import statistics
        import torch

        from pocket_tts.utils.utils import display_execution_time

        stop_event = _get_or_create_stop_event(self)

        # Fast-path early bail if Stop was signalled before the loop ever
        # started — push the sentinel and return.
        if stop_event.is_set():
            try:
                latents_queue.put(None)
            except Exception:  # pragma: no cover - best-effort sentinel
                logger.exception(
                    "main-046: pre-loop latents_queue sentinel push failed"
                )
            return

        # The original pocket-tts `_autoregressive_generation` carries a
        # `@torch.no_grad` decorator (tts_model.py:744). Replacing the method
        # body bypasses that decorator, so without this `with` block every
        # `_run_flow_lm_and_increment_step` call builds and retains an
        # autograd graph for the full generation — roughly +500 MB per long
        # utterance on top of the steady-state RSS, accumulating across Stop
        # cycles. main-045's prototype called `original(...)` so it inherited
        # the decorator for free; 1.3.0's direct method-body replacement
        # has to restate it explicitly here.
        with torch.no_grad():
            backbone_input = torch.full(
                (1, 1, self.flow_lm.ldim),
                fill_value=float("NaN"),
                device=next(iter(self.flow_lm.parameters())).device,
                dtype=self.flow_lm.dtype,
            )
            steps_times = []
            eos_step = None
            cancelled = False
            for generation_step in range(max_gen_len):
                with display_execution_time("Generating latent", print_output=False) as timer:
                    next_latent, is_eos = self._run_flow_lm_and_increment_step(
                        model_state=model_state, backbone_input_latents=backbone_input
                    )

                    # --- main-046 cancellation hook (the one line that matters) ---
                    # Check the stop event between inference and the latent push.
                    # If set, push the None sentinel below in the cleanup branch
                    # and break out. The decoder thread (`_decode_audio_worker`)
                    # reads None as a poison pill and unwinds; that pushes
                    # ("done", None) onto its own result_queue which lets the
                    # consumer in `_generate_audio_stream_short_text` exit too.
                    if stop_event.is_set():
                        cancelled = True
                        break
                    # --------------------------------------------------------------

                    if is_eos.item() and eos_step is None:
                        eos_step = generation_step
                    if eos_step is not None and generation_step >= eos_step + frames_after_eos:
                        break

                    # Add generated latent to queue for immediate decoding
                    latents_queue.put(next_latent)
                    backbone_input = next_latent
                steps_times.append(timer.elapsed_time_ms)
            else:
                if os.environ.get("KPOCKET_TTS_ERROR_WITHOUT_EOS", "0") == "1":
                    raise RuntimeError("Generation reached maximum length without EOS!")
                logger.warning(
                    "Maximum generation length reached without EOS, this very often "
                    "indicates an error."
                )

            # Add sentinel value to signal end of generation. On the cancelled
            # path this is the only thing the decoder thread needs to unwind.
            try:
                latents_queue.put(None)
            except Exception:  # pragma: no cover - best-effort sentinel
                logger.exception("main-046: terminal latents_queue sentinel push failed")

            if not cancelled and steps_times:
                try:
                    logger.info(
                        "Average generation step time: %d ms",
                        int(statistics.mean(steps_times)),
                    )
                except statistics.StatisticsError:  # pragma: no cover - defensive
                    pass

    # Preserve the function metadata so introspection-driven code (e.g.
    # tracebacks, debuggers) still names the original.
    patched.__name__ = getattr(original, "__name__", "_autoregressive_generation")
    patched.__qualname__ = getattr(
        original, "__qualname__", "TTSModel._autoregressive_generation"
    )
    patched.__doc__ = (original.__doc__ or "") + (
        "\n\n[utterheim_sidecar main-046: replaced with stop-event-aware "
        "reimplementation per ADR 0027 option (e). Reads "
        "self._utterheim_stop_event between inference steps; on Stop, pushes "
        "the None sentinel to latents_queue and returns.]"
    )
    # Stash the original so we can verify the patch identity at startup and
    # so a future test or debug path can call the unpatched code if needed.
    patched._utterheim_original = original  # type: ignore[attr-defined]

    target_class._autoregressive_generation = patched

    # Sanity-check: confirm the patch actually replaced the attribute. Catches
    # __slots__ / descriptor rebind rejection that would silently no-op.
    if getattr(target_class, "_autoregressive_generation", None) is not patched:
        raise RuntimeError(
            "main-046 cancellation patch: monkey-patch assignment did not "
            "stick. TTSModel may use slots or a descriptor that rejects "
            "rebinding. Investigate before shipping."
        )

    return (
        "patched pocket_tts.models.tts_model.TTSModel._autoregressive_generation "
        "with direct stop-event-aware reimplementation (main-046 / ADR 0027 option e)"
    )


async def _disconnect_aware_iterator(request: Request, source, model):
    """Wrap a synchronous WAV-byte generator with a disconnect poll.

    `source` is the underlying generator returned by
    `pocket_tts.main.generate_data_with_state(...)`. It yields bytes (or
    bytes-like chunks) synchronously, driven by an internal producer thread.

    We iterate it from a threadpool worker (StreamingResponse already runs
    sync iterators on a thread), polling `await request.is_disconnected()`
    between yields. On disconnect we set the per-model stop event so the
    patched `_autoregressive_generation` breaks out within one inference
    step.

    Critical-fix history (main-045 measurement campaign, propagated to
    bundled source at 1.2.2 and inherited here):
      - The finally block MUST set `stop_event` unconditionally. Without
        this, Starlette cancelling our `to_thread(next, iterator)` raises
        CancelledError BEFORE the disconnect poll can fire — the event is
        never set and the producer thread runs to completion.
      - The finally block MUST NOT call `source.close()`. When
        `to_thread(next, iterator)` is mid-execution on a worker thread,
        close() from this thread raises "generator already executing" and
        swallows the cancellation chain.
    The patched generation loop observes the event on its next iteration,
    breaks, pushes the H4 sentinel, the producer thread exits, the consumer
    drains, the source generator unwinds naturally.
    """
    stop_event = _get_or_create_stop_event(model)
    # Make sure we don't inherit a previously-set event from a prior request.
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
                        "utterheim_sidecar: /tts-with-state client disconnected "
                        "mid-stream; signalling cancellation."
                    )
                    stop_event.set()
                    break
    finally:
        # Set unconditionally — covers CancelledError before disconnect poll fired.
        # Do NOT call source.close(): races with to_thread(next, iterator) →
        # "generator already executing". The patched generation loop sees the
        # event on its next iteration and unwinds the chain itself.
        stop_event.set()
        # Do NOT clear() here — the patched generation loop may still be
        # observing it on another thread. The next request's wrapper entry
        # clears the event before kicking off synthesis.


class LanguageRoutingMiddleware(BaseHTTPMiddleware):
    """Read the C# host's `X-Voice-Language` header and swap
    `pocket_tts.main.tts_model` to the matching resident model before the
    pocket-tts `/tts` handler (or our `/tts-with-state` handler) runs.

    If the header is absent we fall back to `_DEFAULT_LANGUAGE` (the first
    `--language` passed to `serve`) so the pre-main-039 wire shape (no
    header) still works. If the header names a language no resident model
    was preloaded for we return a structured 503 JSON envelope that names
    the missing language — never a process crash (AC 5 of main-039).

    Cancellation hook (main-046): when the route is `/tts` we wrap the
    StreamingResponse's body_iterator with a disconnect-aware iterator
    that sets the per-model stop event on client disconnect. The
    `/tts-with-state` route wraps itself at the handler level via
    `_disconnect_aware_iterator` (the same primitive).
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

        # main-046: cancellation hook on /tts. Clear the per-model stop
        # event before the inner pocket-tts handler kicks off synthesis,
        # then wrap the response body iterator so a mid-stream disconnect
        # sets the event and the patched _autoregressive_generation breaks
        # out within one inference step.
        if request.url.path == "/tts":
            _get_or_create_stop_event(model).clear()

            response = await call_next(request)
            body = getattr(response, "body_iterator", None)
            if body is not None:
                async def _wrapped_async_body():
                    # response.body_iterator is async; pump bytes through
                    # the disconnect poll.
                    last_poll = time.monotonic()
                    stop_event = _get_or_create_stop_event(model)
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
                                        "utterheim_sidecar: /tts client "
                                        "disconnected mid-stream; signalling "
                                        "cancellation."
                                    )
                                    stop_event.set()
                                    break
                    finally:
                        # Mirror _disconnect_aware_iterator: set unconditionally
                        # (covers CancelledError before the poll fired) and skip
                        # any close() on the inner iterator (races with the
                        # producer thread).
                        stop_event.set()

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

        # main-046: always wrap with the disconnect-aware iterator. The
        # wrapper polls request.is_disconnected() between yields and sets
        # the per-model stop event so the patched _autoregressive_generation
        # breaks out of its inner loop on client disconnect.
        underlying = generator(text, state)
        stream = _disconnect_aware_iterator(request, underlying, tts_model)
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

    main-046: the cancellation patch is applied before any model load.
    A startup sanity check refuses to bring up the sidecar if pocket-tts
    has shifted its `_autoregressive_generation` shape (clear error → C#
    bootstrap dialog rather than a half-running sidecar that leaks RSS).
    """
    from pocket_tts import TTSModel
    from pocket_tts import main as pocket_main

    # main-046: cancellation is always on. Patch first, fail fast on a
    # pocket-tts API shift — this is cheaper to triage than a mid-load crash.
    # Use print(flush=True) NOT logger.info: the `utterheim_sidecar` named
    # logger has no handler attached and Python's default level is WARNING,
    # so INFO calls are silently dropped at this point in startup.
    print(
        "utterheim_sidecar: applying ADR 0027 option (e) cancellation patch...",
        flush=True,
    )
    patch_desc = _patch_autoregressive_generation()
    print(f"utterheim_sidecar: cancellation patch installed — {patch_desc}", flush=True)

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
