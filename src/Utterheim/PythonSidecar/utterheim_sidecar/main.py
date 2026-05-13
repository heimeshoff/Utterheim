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
from typing import Optional

import typer
from fastapi import File, Form, HTTPException, UploadFile
from fastapi.responses import FileResponse, StreamingResponse
from starlette.background import BackgroundTask

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


def _get_resident_tts_model():
    """Return the pocket-tts resident TTSModel that `serve` loaded once at startup."""
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
    language: Optional[str] = typer.Option(None, help="Override the model language."),
    config: Optional[str] = typer.Option(None, help="Override the model config name."),
    quantize: bool = typer.Option(False, help="Use the int8-quantised model."),
):
    """Start the utterheim-wrapped pocket-tts FastAPI app.

    Loads pocket-tts's TTSModel into `pocket_tts.main.tts_model` exactly the
    way pocket-tts's own serve command does, then hands control to uvicorn
    against the same `web_app` (now also carrying our /export-voice and
    /tts-with-state routes).
    """
    # Load the model and stash it in the module-level slot pocket-tts's
    # /tts handler reads from. We do this via TTSModel.load_model rather
    # than calling pocket_tts.main.serve() directly because serve() ends
    # in uvicorn.run() and would block before we got a chance to use it.
    from pocket_tts import TTSModel
    from pocket_tts import main as pocket_main

    load_kwargs = {}
    if language is not None:
        load_kwargs["language"] = language
    if config is not None:
        load_kwargs["config"] = config
    if quantize:
        load_kwargs["quantize"] = True

    logger.info("utterheim_sidecar: loading pocket-tts model %s", load_kwargs or "(defaults)")
    try:
        model = TTSModel.load_model(**load_kwargs)
    except TypeError:
        # If pocket-tts's load_model doesn't accept one of these kwargs in this
        # release, fall back to the no-arg form so the sidecar still starts.
        # The CLI flags become best-effort rather than hard requirements.
        logger.warning(
            "TTSModel.load_model(**%s) rejected one or more kwargs; "
            "falling back to defaults.",
            load_kwargs,
        )
        model = TTSModel.load_model()

    # Publish the resident model into pocket_tts.main's namespace so its /tts
    # handler (which reads `tts_model` as a module global) keeps working.
    setattr(pocket_main, "tts_model", model)

    import uvicorn

    # Bind to the same FastAPI app the wrapper has now decorated with the
    # /export-voice and /tts-with-state routes. Uvicorn's startup banner
    # ("Uvicorn running on http://127.0.0.1:NNNN") matches the SidecarHost
    # PortRegex unchanged.
    uvicorn.run(web_app, host=host, port=port, log_level="info")


if __name__ == "__main__":  # pragma: no cover - module entry point
    app()
