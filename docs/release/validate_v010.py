"""Validate original checkpoint/ONNX parity and frozen Player without training."""
from pathlib import Path
import sys, json, hashlib
import numpy as np
import torch
from onnx.reference import ReferenceEvaluator

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / 'release-staging/dino-attack-flow-rl'
ARTIFACT_ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT
sys.path.insert(0, str(PUBLIC / 'flow_rl/src'))
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.export.dino_policy_onnx import _trained_module
from flow_rl.tracking.checkpoint import validate_flow_bc_checkpoint
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel

OUT = ROOT / 'reports/windows-rebuild-20260906/release-validation'
OUT.mkdir(parents=True, exist_ok=True)
torch.set_num_threads(2)
protocol = DinoProtocol.from_yaml(PUBLIC / 'flow_rl/configs/dino_attack_structured_set_v2.yaml')
dataset = np.load(ARTIFACT_ROOT / 'reports/phase8/policyflow-training/ppo-teacher-success-dataset-seed20260905-500x2-16env-20260905-a3/dataset/demonstrations.npz')
assert len(dataset['actions']) == 57459
assert np.isfinite(dataset['actions']).all() and np.max(np.abs(dataset['actions'])) <= 1
observations = tuple(dataset[f'observations_{i}'][::10000].copy() for i in range(6))
result = {'models': [], 'dataset_samples': len(dataset['actions'])}
for model in json.loads((PUBLIC / 'artifacts.json').read_text())['models']:
    onnx_path = PUBLIC / model['path']
    manifest = json.loads(onnx_path.with_suffix('.manifest.json').read_text())
    cp = ARTIFACT_ROOT / Path(manifest['provenance']['checkpoint']['file']).relative_to(ROOT)
    assert hashlib.sha256(cp.read_bytes()).hexdigest() == manifest['provenance']['checkpoint']['sha256']
    module, _, mode, _ = _trained_module(protocol, cp)
    module.eval()
    with torch.inference_mode(): expected = module(*(torch.from_numpy(o) for o in observations)).numpy()
    actual = ReferenceEvaluator(str(onnx_path)).run(None, dict(zip(('global','regions','walls','guards','houses','dinos'), observations)))[0]
    np.testing.assert_allclose(actual, expected, atol=2e-5, rtol=2e-5)
    assert np.isfinite(actual).all() and np.max(np.abs(actual)) <= 1
    result['models'].append({'algorithm': model['algorithm'], 'mode': mode, 'batch': len(actual), 'max_error': float(np.max(np.abs(actual - expected)))})
    print(model['algorithm'], 'parity PASS', flush=True)

bc = ARTIFACT_ROOT / 'reports/phase8/policyflow-training/flow-bc-set-transformer-seed0-20260905-a2/checkpoints/best.pt'
validate_flow_bc_checkpoint(torch.load(bc, map_location='cpu', weights_only=False))
result['flow_bc_checkpoint'] = 'PASS'
if '--no-player' in sys.argv:
    (OUT / 'extracted-model-validation.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    raise SystemExit(0)

channel = EngineConfigurationChannel()
channel.set_configuration_parameters(time_scale=1, target_frame_rate=-1, capture_frame_rate=0, quality_level=0)
build = ARTIFACT_ROOT / 'reports/phase8/ppo-baseline/build-fixed-clock-v5-20260903/DinoAttackDualMapPPOV2-FixedClockV5-20260903.exe'
(OUT / 'unity').mkdir(exist_ok=True)
rng = np.random.default_rng(20260906)
terminals = 0
truncated = 0
decisions = 0
with UnityEnvAdapter(build, worker_id=5700, seed=20260906, no_graphics=True,
                     timeout_wait=90, protocol=protocol, log_folder=OUT / 'unity', side_channels=[channel],
                     additional_args=['--dino-training','--dino-base-seed','20260906','--dino-environment-index','0','--dino-process-generation','0']) as env:
    step = env.reset()
    assert env.continuous_action_size == 4
    assert env.observation_shapes == ((5,), (5,8), (6,5), (11,7), (8,6), (10,7))
    for i in range(600):
        count = len(env.pending_agent_ids)
        step = env.step(rng.uniform(-1, 1, (count,4)).astype(np.float32))
        decisions += count
        terminals += int(step.terminated.sum())
        truncated += int(step.truncated.sum())
        if terminals >= 4: break
    assert terminals >= 4 and truncated == 0
    result['player'] = {'behavior': env.behavior_name, 'shapes': env.observation_shapes,
                        'action_size': 4, 'decisions': decisions, 'natural_terminals': terminals, 'truncated': truncated}
(OUT / 'validation.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
