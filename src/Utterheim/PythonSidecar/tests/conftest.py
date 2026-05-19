"""Shared pytest fixtures for utterheim_sidecar tests.

These tests deliberately avoid importing the *real* `pocket_tts` package
(heavy: torch, safetensors, mimi, ~600 MB on disk). Instead each test stubs
the minimal `pocket_tts.models.tts_model` surface that
`utterheim_sidecar.main._patch_autoregressive_generation` reaches for.

The tricky bit: `utterheim_sidecar.main` imports `pocket_tts.main` at module
import time (it needs `web_app`). To keep that working without paying the
real pocket-tts boot cost we install module stubs in `sys.modules` BEFORE
the test runner imports `utterheim_sidecar.main`. `_stub_pocket_tts` below
is invoked from each test that needs the patch helper.
"""

from __future__ import annotations

import os
import sys
import types
from pathlib import Path


# Make the bundled `utterheim_sidecar` package importable without installing
# it. The package directory lives one level up from this `tests/` folder.
_PYTHON_SIDECAR_DIR = Path(__file__).resolve().parent.parent
if str(_PYTHON_SIDECAR_DIR) not in sys.path:
    sys.path.insert(0, str(_PYTHON_SIDECAR_DIR))


def install_pocket_tts_stub(*, with_method: bool = True, method=None):
    """Install a fake `pocket_tts` package tree in `sys.modules`.

    Args:
        with_method: when True, `TTSModel._autoregressive_generation` is
            defined on the stub class with the expected signature. When
            False, the method is omitted so we can assert the sanity check
            fails loudly.
        method: optional callable to use as the original method body. If
            None, a no-op is installed.

    Returns the stub `TTSModel` class so the test can inspect it.
    """
    # Remove any prior leftovers so successive tests don't see stale state.
    for mod in (
        "pocket_tts",
        "pocket_tts.main",
        "pocket_tts.models",
        "pocket_tts.models.tts_model",
    ):
        sys.modules.pop(mod, None)

    pocket_tts = types.ModuleType("pocket_tts")
    pocket_tts.__path__ = []  # mark as a package

    pocket_tts_main = types.ModuleType("pocket_tts.main")
    # `utterheim_sidecar.main` does `from pocket_tts.main import web_app`.
    # We don't need a real FastAPI app here — but we do need *something*
    # `add_middleware` and `post` can be called on without exploding.
    from fastapi import FastAPI

    pocket_tts_main.web_app = FastAPI()
    pocket_tts_main.tts_model = None
    pocket_tts_main.generate_data_with_state = None

    pocket_tts_models = types.ModuleType("pocket_tts.models")
    pocket_tts_models.__path__ = []  # mark as a package

    pocket_tts_models_tts_model = types.ModuleType("pocket_tts.models.tts_model")

    # Stub `pocket_tts.utils.utils.display_execution_time`. The patched body
    # imports it inside the function (so the patch helper itself stays
    # torch-free) but a stubbed pocket-tts has no real utils package.
    pocket_tts_utils = types.ModuleType("pocket_tts.utils")
    pocket_tts_utils.__path__ = []
    pocket_tts_utils_utils = types.ModuleType("pocket_tts.utils.utils")

    class _NoOpTimer:
        elapsed_time_ms = 0

        def __enter__(self):
            return self

        def __exit__(self, *exc):
            return False

    def display_execution_time(*_args, **_kwargs):
        return _NoOpTimer()

    pocket_tts_utils_utils.display_execution_time = display_execution_time
    pocket_tts_utils.utils = pocket_tts_utils_utils

    class TTSModel:
        # Mimic enough surface that `_get_or_create_stop_event(self)` can
        # attach `_utterheim_stop_event` to instances.
        pass

    if with_method:
        if method is None:
            def _autoregressive_generation(
                self, model_state, max_gen_len, frames_after_eos, latents_queue
            ):  # noqa: D401 - signature shape matters more than docstring
                """Stub matching pocket-tts 2.x signature."""
                return None

            method = _autoregressive_generation
        TTSModel._autoregressive_generation = method

    pocket_tts_models_tts_model.TTSModel = TTSModel
    pocket_tts.main = pocket_tts_main
    pocket_tts.models = pocket_tts_models

    pocket_tts.utils = pocket_tts_utils

    sys.modules["pocket_tts"] = pocket_tts
    sys.modules["pocket_tts.main"] = pocket_tts_main
    sys.modules["pocket_tts.models"] = pocket_tts_models
    sys.modules["pocket_tts.models.tts_model"] = pocket_tts_models_tts_model
    sys.modules["pocket_tts.utils"] = pocket_tts_utils
    sys.modules["pocket_tts.utils.utils"] = pocket_tts_utils_utils

    # Strip the wrapper's cached import so each test starts from a clean
    # patch slate.
    sys.modules.pop("utterheim_sidecar", None)
    sys.modules.pop("utterheim_sidecar.main", None)

    return TTSModel
