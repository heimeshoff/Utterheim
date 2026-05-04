# Mockingbird-owned Python sidecar wrapper around pocket-tts.
#
# Per ADR 0015 (.agenthoff/knowledge/decisions/0015-mockingbird-sidecar-wrapper.md)
# this package mounts two extra HTTP routes on top of pocket-tts's existing
# FastAPI app so that voice cloning ("/export-voice") and synthesis with a
# previously-exported voice state ("/tts-with-state") can reuse the resident
# TTSModel instance instead of paying the ~10-30 s import + load cost on every
# request.
#
# The package intentionally has zero pip dependencies of its own: every Python
# import it makes is satisfied transitively by `pocket-tts` (fastapi, typer,
# uvicorn, torch, safetensors, etc.).
#
# Entry point: `python -m mockingbird_sidecar serve --host 127.0.0.1 --port 0`
# The C# SidecarHost spawns this exact command line.

# Bump this string whenever any wrapper file (main.py, __main__.py, this file)
# changes behaviourally — the C# bootstrapper compares bundled vs installed
# __version__ at launch and forces a re-install on mismatch (main-027).
# The string is opaque equality; no semver constraint solver involved.
__version__ = "1.0.1"
