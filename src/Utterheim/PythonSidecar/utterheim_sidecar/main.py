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

import logging
import os
import tempfile
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
        return StreamingResponse(
            generator(text, state),
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
