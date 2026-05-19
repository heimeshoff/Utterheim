"""test_sanity_check.py — the loud-fail guard.

main-046 AC: with a pocket-tts that lacks `_autoregressive_generation` (e.g.
upstream renamed or removed the method), `_patch_autoregressive_generation`
MUST raise `RuntimeError` rather than silently no-op. This is the
sidecar-startup early-bail check that refuses to bring up a sidecar with
broken cancellation wiring.
"""

from __future__ import annotations

import pytest

from conftest import install_pocket_tts_stub


def _import_wrapper_fresh():
    import importlib
    import utterheim_sidecar.main as wrapper

    importlib.reload(wrapper)
    return wrapper


def test_missing_autoregressive_generation_raises():
    install_pocket_tts_stub(with_method=False)
    wrapper = _import_wrapper_fresh()
    with pytest.raises(RuntimeError, match="_autoregressive_generation"):
        wrapper._patch_autoregressive_generation()


def test_missing_ttsmodel_class_raises():
    """If the whole TTSModel class vanishes from the module, refuse."""
    install_pocket_tts_stub(with_method=False)
    import sys

    mod = sys.modules["pocket_tts.models.tts_model"]
    del mod.TTSModel

    wrapper = _import_wrapper_fresh()
    with pytest.raises(RuntimeError, match="TTSModel"):
        wrapper._patch_autoregressive_generation()
