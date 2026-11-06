"""Fixed upstream self-play checkpoint conversion and NumPy CPU inference."""
import hashlib
import io
import os
from pathlib import Path
import pickle
import tempfile

import numpy as np

SP_ID = "sp-cramped-room"
CHECKPOINT = "src/overcooked_demo/server/static/assets/agents/RllibCrampedRoomSP/agent/checkpoint-550"
CHECKPOINT_SHA256 = "95f9b5f9e3a9a5a0cbefe21a4d1517d5da71b0c93a4f79dda2f8c7b97077f322"
LAYERS = {
    "conv_initial": (5, 5, 26, 25),
    "conv_0": (3, 3, 25, 25),
    "conv_1": (3, 3, 25, 25),
    "dense": (150, 64),
    "dense_1": (64, 64),
    "dense_2": (64, 64),
    "dense_3": (64, 6),
}


class _Metadata:
    """Stand-in for old Ray, TensorFlow and Gym classes in the verified pickle."""
    def __init__(self, *args, **kwargs):
        pass

    def __call__(self, *args, **kwargs):
        return _Metadata()

    def __setstate__(self, state):
        pass


class _CheckpointReader(pickle.Unpickler):
    def find_class(self, module, name):
        if module.startswith("numpy") or module == "collections":
            return super().find_class(module, name)
        return _Metadata


def _check_tensors(weights):
    for layer, kernel_shape in LAYERS.items():
        for suffix, shape in (("kernel", kernel_shape), ("bias", kernel_shape[-1:])):
            array = weights[f"{layer}/{suffix}"]
            if array.dtype != np.float32 or array.shape != shape or not np.isfinite(array).all():
                raise ValueError(f"Invalid SP tensor {layer}/{suffix}")


def prepare_sp(upstream_root, models_dir):
    """Only unpickle the exact known bytes from the explicitly supplied local checkout."""
    raw = (Path(upstream_root) / CHECKPOINT).read_bytes()
    if hashlib.sha256(raw).hexdigest() != CHECKPOINT_SHA256:
        raise ValueError("Unexpected upstream SP checkpoint SHA256")
    inner = pickle.loads(raw)["worker"]
    worker = _CheckpointReader(io.BytesIO(inner)).load()
    weights = {key.removeprefix("ppo/"): value for key, value in worker["state"]["ppo"]["weights"].items()
               if key.removeprefix("ppo/").split("/")[0] in LAYERS}
    _check_tensors(weights)
    destination = Path(models_dir) / f"{SP_ID}.npz"
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as scratch:
        scratch_path = Path(scratch.name)
    try:
        with scratch_path.open("wb") as stream:
            np.savez_compressed(stream, **weights)
        os.replace(scratch_path, destination)
    finally:
        scratch_path.unlink(missing_ok=True)
    return destination


class SelfPlayPolicy:
    def __init__(self, path, mdp, index, seed, horizon):
        self.mdp, self.index, self.horizon = mdp, index, horizon
        self.rng = np.random.default_rng(seed)
        with np.load(path, allow_pickle=False) as archive:
            _check_tensors(archive)
            self.weights = {key: archive[key].astype(np.float64) for key in archive.files}

    def logits(self, obs):
        if obs.shape != (5, 4, 26):
            raise ValueError(f"SP requires lossless observation (5,4,26), got {obs.shape}")
        x = obs.astype(np.float64)
        for name, padding in (("conv_initial", 2), ("conv_0", 1), ("conv_1", 0)):
            kernel = self.weights[name + "/kernel"]
            if padding:
                x = np.pad(x, ((padding, padding), (padding, padding), (0, 0)))
            windows = np.lib.stride_tricks.sliding_window_view(x, kernel.shape[:2], axis=(0, 1))
            x = np.einsum("xycij,ijco->xyo", windows, kernel) + self.weights[name + "/bias"]
            x = np.where(x >= 0, x, x * .2)
        x = x.reshape(-1)  # Original Keras Flatten uses NHWC order.
        for name in ("dense", "dense_1", "dense_2"):
            x = x @ self.weights[name + "/kernel"] + self.weights[name + "/bias"]
            x = np.where(x >= 0, x, x * .3)
        return x @ self.weights["dense_3/kernel"] + self.weights["dense_3/bias"]

    def act(self, state):
        observation = self.mdp.lossless_state_encoding(state, horizon=self.horizon)[self.index]
        logits = self.logits(observation)
        probabilities = np.exp(logits - logits.max())
        return int(self.rng.choice(6, p=probabilities / probabilities.sum()))
