from pathlib import Path

import onnx
import torch
import pytest
from onnx.reference import ReferenceEvaluator

from flow_rl.export.dino_policy_onnx import export_trained_checkpoint
from flow_rl.cli.train_dino_parallel_policyflow import _build_policy_and_state
from flow_rl.training.dino_parallel_policyflow_config import DinoParallelPolicyFlowConfig
from flow_rl.envs.dino_protocol import DinoProtocol


def test_policyflow_export_matches_zero_noise_midpoint(tmp_path):
    root = Path(__file__).resolve().parents[3]
    checkpoint = root / 'reports/phase8/policyflow-training/resume-step836064-seed0-20260905-a1/checkpoints/step-2147452.pt'
    if not checkpoint.exists():
        pytest.skip('Local deployment checkpoint is not present')
    payload = torch.load(checkpoint, map_location='cpu', weights_only=False)
    config = DinoParallelPolicyFlowConfig(**{k: (Path(v) if k.endswith('_path') or k in ('run_directory', 'resume_checkpoint') else tuple(v) if isinstance(v, list) else v) for k,v in payload['config'].items() if k in DinoParallelPolicyFlowConfig.__dataclass_fields__})
    protocol = DinoProtocol.from_yaml(config.protocol_path)
    policy, *_ = _build_policy_and_state(config, protocol, torch.device('cpu'))
    policy.load_state_dict(payload['model_state'])
    policy.eval()
    result = export_trained_checkpoint(output_path=tmp_path/'policyflow.onnx', protocol_path=config.protocol_path, checkpoint_path=checkpoint)
    onnx.checker.check_model(onnx.load(result.model_path))
    evaluator = ReferenceEvaluator(str(result.model_path))
    for batch in (1, 3):
        obs = tuple(torch.zeros((batch, *s)) for s in protocol.observation_shapes)
        with torch.inference_mode():
            expected = torch.tanh(policy.sample_prior(obs, initial_noise=torch.zeros(batch,4)).latent_actions)
            actual = result.module(*obs)
        torch.testing.assert_close(actual, expected)
        exported = evaluator.run(None, dict(zip(('global','regions','walls','guards','houses','dinos'), (x.numpy() for x in obs))))[0]
        torch.testing.assert_close(torch.from_numpy(exported), expected, atol=1e-5, rtol=1e-4)
        assert torch.isfinite(actual).all() and actual.abs().max() <= 1
