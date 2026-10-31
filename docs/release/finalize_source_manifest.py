from pathlib import Path
import subprocess, json, hashlib, re, zipfile

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / 'release-staging/dino-attack-flow-rl'
def git(*args):
    return subprocess.check_output(['git','-C',str(PUBLIC),*args])
paths = git('ls-files','-z').decode().split('\0')
unstaged = set(git('diff','--name-only','-z').decode().split('\0'))
patterns = [rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
            rb'gh[pousr]_[A-Za-z0-9]{30,}', rb'github_pat_[A-Za-z0-9_]{30,}',
            rb'AKIA[A-Z0-9]{16}']
manifest = {'schema_version':1, 'notes':'SHA-256 of committed source bytes; excludes this manifest. Generated local Unity changes are not committed.', 'files':[]}
for name in paths:
    if not name or name == 'source-manifest.json': continue
    data = git('show', ':' + name) if name in unstaged else (PUBLIC / name).read_bytes()
    if any(re.search(pattern, data) for pattern in patterns): raise RuntimeError('Credential pattern in ' + name)
    manifest['files'].append({'path':name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
assert b'160000' not in git('ls-files','--stage')
for path in (ROOT / 'release-staging/artifacts/v0.1.0').glob('*.zip'):
    with zipfile.ZipFile(path) as archive:
        for name in archive.namelist():
            if Path(name).suffix in ('.json','.md','.csv','.txt','.yaml','.py'):
                data = archive.read(name)
                if any(re.search(pattern, data) for pattern in patterns): raise RuntimeError('Credential pattern in attachment ' + name)
(PUBLIC / 'source-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'files':len(manifest['files']), 'credential_scan':'PASS', 'gitlinks':'none', 'excluded_local_changes':sorted(unstaged - {'','source-manifest.json'})}))
