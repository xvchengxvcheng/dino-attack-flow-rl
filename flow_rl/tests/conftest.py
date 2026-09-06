from pathlib import Path
import hashlib
import pytest
import torch
from flow_rl.models.flow import ConditionalVelocityMLP

@pytest.fixture
def flow_bc_fixture(tmp_path: Path):
    """Small generated checkpoint for unit tests; no Unity or trained artifacts required."""
    build = tmp_path / "unit-test-player.exe"
    build.write_bytes(b"unit-test sentinel; not an executable")
    with torch.random.fork_rng():
        torch.manual_seed(321)
        actor = ConditionalVelocityMLP(observation_shapes=((8,),), action_size=2)
    payload = {
        "model_state": actor.state_dict(), "optimizer_state": {},
        "normalizer_state": {},
        "config": {"state_size": 128, "time_embedding_size": 32,
                   "velocity_hidden_sizes": [128, 128]},
        "metadata": {
            "schema_version": 1, "observation_shapes": [[8]], "action_size": 2,
            "solver": {"method": "euler", "direction": "0_to_1", "nfe": 4,
                       "action_transform": "clamp"},
            "algorithm": "flow_bc", "dataset_sha256": "unit-test-dataset",
            "metadata_sha256": "unit-test-metadata",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
        },
    }
    checkpoint = tmp_path / "flow-bc.pt"
    torch.save(payload, checkpoint)
    return build, checkpoint
