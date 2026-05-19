"""test_cancel_patch.py — patch installs and honours the stop_event.

main-046 AC: `_patch_autoregressive_generation` replaces TTSModel's method
with one that observes `model._utterheim_stop_event` and returns within
≤200 ms of the event being set.
"""

from __future__ import annotations

import queue
import threading
import time

import pytest

from conftest import install_pocket_tts_stub


def _import_wrapper_fresh():
    """Re-import utterheim_sidecar.main after stubs are in place."""
    import importlib
    import utterheim_sidecar.main as wrapper

    importlib.reload(wrapper)
    return wrapper


def test_patch_replaces_method():
    """After `_patch_autoregressive_generation()` the method on the class
    is the new wrapper, not the stub's original."""
    target_class = install_pocket_tts_stub()
    original = target_class._autoregressive_generation

    wrapper = _import_wrapper_fresh()
    wrapper._patch_autoregressive_generation()

    assert target_class._autoregressive_generation is not original
    assert getattr(target_class._autoregressive_generation, "__name__", "") == (
        "_autoregressive_generation"
    )


def test_stop_event_breaks_loop_within_200ms():
    """The patched method must return promptly after stop_event is set.

    We don't have the real pocket-tts `_run_flow_lm_and_increment_step`
    available, so the test installs a stub that simulates the inner loop:
    each "step" sleeps ~5 ms and pushes a fake latent onto the queue. The
    patched body must observe the event between steps and break out.
    """
    target_class = install_pocket_tts_stub()

    # Give the stub TTSModel a `_run_flow_lm_and_increment_step` returning
    # a (latent, is_eos) tuple. The patched body calls this on every step.
    class _FakeBool:
        def __init__(self, value):
            self._value = value

        def item(self):
            return self._value

    def _run_flow_lm_and_increment_step(self, model_state, backbone_input_latents):
        time.sleep(0.005)  # 5 ms per "step" — proxy for one inference tick
        return ("latent-placeholder", _FakeBool(False))

    target_class._run_flow_lm_and_increment_step = _run_flow_lm_and_increment_step
    # `flow_lm` has `.ldim`, `.dtype` and `.parameters()`. The patched body
    # touches them to build the initial `backbone_input` tensor. We can
    # short-circuit that with a lightweight stand-in if the patched body
    # tolerates it; otherwise the patched body must be tolerant of stubs.
    # The patched body uses torch.full → which needs a torch.dtype and a
    # device. To keep this test torch-free we install torch *only if not
    # already present* and ask the wrapper to skip the tensor allocation
    # path on a missing torch.

    # The patched body in production uses `torch.full(...)`. We can avoid
    # importing torch by setting up a minimal flow_lm whose parameters()
    # returns a placeholder; the patched body will then call torch.full on
    # `next(iter(...))`. We require torch to be importable.
    try:
        import torch
    except ImportError:  # pragma: no cover - torch is in the runtime
        pytest.skip("torch not installed in this interpreter")

    class _FakeFlowLM:
        ldim = 4
        dtype = torch.float32

        def parameters(self):
            # one fake parameter with device='cpu'
            return iter([torch.zeros(1, device="cpu")])

    instance = target_class()
    instance.flow_lm = _FakeFlowLM()

    wrapper = _import_wrapper_fresh()
    wrapper._patch_autoregressive_generation()

    stop_event = wrapper._get_or_create_stop_event(instance)
    stop_event.clear()

    latents_queue: "queue.Queue" = queue.Queue()
    result: dict = {"returned_at": None}

    def _runner():
        target_class._autoregressive_generation(
            instance,
            {},  # model_state
            10_000,  # max_gen_len — high so we don't break on count
            0,  # frames_after_eos
            latents_queue,
        )
        result["returned_at"] = time.monotonic()

    t = threading.Thread(target=_runner, daemon=True)
    t.start()
    # Let a few steps run before signalling stop, so we exercise the
    # mid-loop event check.
    time.sleep(0.05)
    set_at = time.monotonic()
    stop_event.set()
    t.join(timeout=1.0)

    assert not t.is_alive(), "patched method did not return after stop_event.set()"
    elapsed_ms = (result["returned_at"] - set_at) * 1000.0
    assert elapsed_ms < 200.0, (
        f"patched method took {elapsed_ms:.1f} ms to return after Stop "
        f"(budget: 200 ms — ADR 0026/0027)"
    )


def test_sanity_check_signature_first_param_self():
    """The patch refuses a method whose first param is not `self`."""

    def bad_signature(not_self, *args, **kwargs):
        return None

    target_class = install_pocket_tts_stub(with_method=False)
    target_class._autoregressive_generation = bad_signature

    wrapper = _import_wrapper_fresh()
    with pytest.raises(RuntimeError, match="first parameter is not 'self'"):
        wrapper._patch_autoregressive_generation()
