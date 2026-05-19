"""test_sentinel_push.py — cancellation pushes None onto the latents_queue
positional arg (NOT `self.latents_queue`).

main-045 H4 fix: the queues live as locals in
`_generate_audio_stream_short_text` and are passed positionally as the 4th
arg to `_autoregressive_generation`. The cancellation path MUST push to
that arg — pushing to a non-existent `self.latents_queue` attribute is a
silent no-op and the decoder thread deadlocks.
"""

from __future__ import annotations

import queue
import threading
import time

import pytest

from conftest import install_pocket_tts_stub


def _import_wrapper_fresh():
    import importlib
    import utterheim_sidecar.main as wrapper

    importlib.reload(wrapper)
    return wrapper


def test_cancellation_pushes_none_to_latents_queue_arg():
    target_class = install_pocket_tts_stub()

    class _FakeBool:
        def __init__(self, value):
            self._value = value

        def item(self):
            return self._value

    def _run_flow_lm_and_increment_step(self, model_state, backbone_input_latents):
        time.sleep(0.005)
        return ("latent-placeholder", _FakeBool(False))

    target_class._run_flow_lm_and_increment_step = _run_flow_lm_and_increment_step

    try:
        import torch
    except ImportError:  # pragma: no cover
        pytest.skip("torch not installed in this interpreter")

    class _FakeFlowLM:
        ldim = 4
        dtype = torch.float32

        def parameters(self):
            return iter([torch.zeros(1, device="cpu")])

    instance = target_class()
    instance.flow_lm = _FakeFlowLM()
    # Crucially: instance has NO `latents_queue` attribute. The sentinel
    # push must reach the positional arg below, not `self`.
    assert not hasattr(instance, "latents_queue")

    wrapper = _import_wrapper_fresh()
    wrapper._patch_autoregressive_generation()

    stop_event = wrapper._get_or_create_stop_event(instance)
    stop_event.clear()

    latents_queue: "queue.Queue" = queue.Queue()

    def _runner():
        target_class._autoregressive_generation(
            instance,
            {},  # model_state
            10_000,
            0,
            latents_queue,
        )

    t = threading.Thread(target=_runner, daemon=True)
    t.start()
    time.sleep(0.03)
    stop_event.set()
    t.join(timeout=1.0)
    assert not t.is_alive()

    # The patched method must have pushed exactly one None sentinel on the
    # cancellation path (decoder thread reads this to exit). A few latents
    # may have been pushed before the event fired — drain them and assert
    # the last item is None.
    drained = []
    while True:
        try:
            drained.append(latents_queue.get_nowait())
        except queue.Empty:
            break

    assert drained, "expected at least the None sentinel on the queue"
    assert drained[-1] is None, (
        f"last item on latents_queue should be the None sentinel, got: {drained!r}"
    )
