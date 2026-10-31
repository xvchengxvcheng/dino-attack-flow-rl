from pathlib import Path
import json, shutil, sys
import yaml

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / 'release-staging/dino-attack-flow-rl'
CHECK = ROOT / 'release-staging/install-check-v010'
sys.path.insert(0, str(PUBLIC / 'flow_rl/src'))
from flow_rl.training.dino_parallel_ppo_config import DinoParallelPPOConfig
from flow_rl.training.dino_parallel_fpo_config import DinoParallelFPOConfig
from flow_rl.training.dino_parallel_policyflow_config import DinoParallelPolicyFlowConfig
from flow_rl.training.flow_bc_config import FlowBCTrainingConfig

specs = [
    ('ppo', 'phase8_dino_ppo_fixedclock_v5_20env_seed0_timescale5_rollout20480_20260903.yaml', DinoParallelPPOConfig),
    ('fpo', 'phase8_dino_fpo_fixedclock_v5_16env_seed0_timescale1_rollout16384_20260904.yaml', DinoParallelFPOConfig),
    ('policyflow', 'phase8_dino_policyflow_fixedclock_v5_16env_seed0_4718592_std03_actorlr5e6_20260905_a2.yaml', DinoParallelPolicyFlowConfig),
    ('flow_bc', 'dino_flow_bc_set_transformer_seed0.yaml', FlowBCTrainingConfig),
]
destination = CHECK / 'flow_rl/configs'
destination.mkdir(parents=True, exist_ok=True)
shutil.copy2(PUBLIC / 'flow_rl/configs/dino_attack_structured_set_v2.yaml', destination)
results = []
for name, original, cls in specs:
    config = yaml.safe_load((PUBLIC / 'flow_rl/configs' / original).read_text())
    config['run_directory'] = '../../runs/v010-' + name + '-example'
    filename = 'release_' + name + '_example.yaml'
    path = PUBLIC / 'flow_rl/configs' / filename
    text = '# Reference parameters only; no training is launched. Change output for each new run.\n' + yaml.safe_dump(config, sort_keys=False)
    path.write_text(text, encoding='utf-8')
    shutil.copy2(path, destination / filename)
    loaded = cls.from_yaml(destination / filename)
    assert loaded.build_path.is_relative_to(CHECK)
    results.append({'algorithm': name, 'configuration': filename, 'installed_artifacts_only': True})
(ROOT / 'reports/windows-rebuild-20260906/release-validation/configurations.json').write_text(json.dumps(results, indent=2))
print(json.dumps(results, indent=2))
