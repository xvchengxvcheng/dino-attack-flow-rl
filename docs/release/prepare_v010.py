"""Prepare immutable release attachments with original relative paths and hashes."""
from pathlib import Path
import hashlib, json, zipfile

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / 'release-staging/dino-attack-flow-rl'
OUT = ROOT / 'release-staging/artifacts/v0.1.0'
OUT.mkdir(parents=True, exist_ok=True)
BASE = 'https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/download/v0.1.0/'

def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b''): h.update(data)
    return h.hexdigest()

def package(name, paths, description):
    paths = sorted(set(paths))
    target = OUT / name
    manifest = {'description': description, 'files': [
        {'path': p.relative_to(ROOT).as_posix(), 'bytes': p.stat().st_size, 'sha256': sha(p)} for p in paths]}
    with zipfile.ZipFile(target, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for p in paths: archive.write(p, p.relative_to(ROOT).as_posix())
        archive.writestr('artifact-manifests/' + name + '.json', json.dumps(manifest, indent=2))
    with zipfile.ZipFile(target) as archive:
        for row in manifest['files']:
            with archive.open(row['path']) as stream:
                h = hashlib.sha256()
                for data in iter(lambda: stream.read(1024 * 1024), b''): h.update(data)
            assert h.hexdigest() == row['sha256'], row['path']
    return {'name': name, 'url': BASE + name, 'bytes': target.stat().st_size,
            'sha256': sha(target), 'description': description, 'file_count': len(paths)}

def tree(directory):
    return [p for p in directory.rglob('*') if p.is_file() and not any('DoNotShip' in part for part in p.parts)]

models = json.loads((PUBLIC / 'artifacts.json').read_text())
checkpoints = []
for m in models['models']:
    manifest = json.loads((PUBLIC / m['path']).with_suffix('.manifest.json').read_text())
    cp = Path(manifest['provenance']['checkpoint']['file'])
    assert sha(cp) == manifest['provenance']['checkpoint']['sha256']
    checkpoints.append(cp)
bc = ROOT / 'reports/phase8/policyflow-training/flow-bc-set-transformer-seed0-20260905-a2'
checkpoints.append(bc / 'checkpoints/best.pt')
dataset = ROOT / 'reports/phase8/policyflow-training/ppo-teacher-success-dataset-seed20260905-500x2-16env-20260905-a3'
training = ROOT / 'reports/phase8/ppo-baseline/build-fixed-clock-v5-20260903'
entries = []
entries.append(package('dino-attack-windows-x64-v0.1.0.zip', tree(ROOT / 'Builds/DinoAttack-Windows-20260906'), 'Latest game Player; PPO/FPO/PolicyFlow, three maps.'))
entries.append(package('dino-training-fixedclock-v5-windows-x64.zip', tree(training), 'Frozen historical training Player; original bytes, not the new game dynamics.'))
entries.append(package('dino-deployed-checkpoints-v0.1.0.zip', checkpoints, 'Three deployed policy checkpoints plus Flow BC best; original state/provenance preserved.'))
entries.append(package('dino-success-demonstrations-v1.zip', tree(dataset / 'dataset') + [dataset / n for n in ('provenance-v2.json', 'action-mode-report-legal-v2.json', 'post-collection-validation.json')], '1000 successful episodes / 57459 samples; includes invalid actions, see legal-action report.'))
run_dirs = [p.parent.parent for p in checkpoints]
run_dirs += [ROOT / 'reports/phase8/policyflow-training' / n for n in (
    'fixed-clock-v5-16env-seed0-4718592-std03-actorlr5e6-20260905-a2',
    'resume-step2147452-seed0-20260905-a1')]
records = []
for run in run_dirs:
    records += [p for p in run.glob('*') if p.is_file() and p.suffix in ('.csv', '.json', '.md', '.yaml')]
for directory in (ROOT / 'reports/phase8/training-comparison-20260905',
                  ROOT / 'reports/phase8/fpo-training/ai-strategy-update160-step2606819-20260905'):
    records += [p for p in directory.rglob('*') if p.is_file() and p.suffix in ('.csv', '.json', '.md', '.png', '.pdf', '.py') and 'checkpoints' not in p.parts]
for p in (ROOT / 'reports/phase8/policyflow-training').rglob('evaluation.json'): records.append(p)
records += [dataset / 'summary.json', dataset / 'effective-config.json']
entries.append(package('dino-experiment-records-v0.1.0.zip', records, 'Selected complete run tables/configuration, frozen evaluations and historical comparison snapshot; not a fair algorithm ranking.'))
models['distribution'] = 'source_with_embedded_onnx_and_release_assets'
models['release_attachments'] = entries
(PUBLIC / 'artifacts.json').write_text(json.dumps(models, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(OUT / 'artifacts.json').write_bytes((PUBLIC / 'artifacts.json').read_bytes())
(OUT / 'SHA256SUMS.txt').write_text(''.join(e['sha256'] + '  ' + e['name'] + '\n' for e in entries) + sha(OUT / 'artifacts.json') + '  artifacts.json\n', encoding='utf-8')
print(json.dumps(entries, indent=2))
