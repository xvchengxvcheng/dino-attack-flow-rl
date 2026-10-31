"""Release transport: credentials stay in memory, artifacts are never overwritten."""
from pathlib import Path
import subprocess, json, sys, hashlib
import httpx

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / 'release-staging/dino-attack-flow-rl'
ART = ROOT / 'release-staging/artifacts/v0.1.0'
API = 'https://api.github.com/repos/xvchengxvcheng/dino-attack-flow-rl'
session = httpx.Client(follow_redirects=True)
session.headers.update({'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'})
mode = sys.argv[1]
if mode != 'verify':
    credential = subprocess.run(['git', 'credential', 'fill'], input='protocol=https\nhost=github.com\nusername=xvchengxvcheng\n\n', capture_output=True, text=True, check=True)
    fields = dict(line.split('=', 1) for line in credential.stdout.splitlines() if '=' in line)
    session.headers['Authorization'] = 'Bearer ' + fields['password']

def request(method, url, **kwargs):
    response = session.request(method, url, timeout=600, **kwargs)
    if not response.is_success: raise RuntimeError(f'{method} {url.split("?")[0]}: HTTP {response.status_code}')
    return response.json()

def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for data in iter(lambda: f.read(1024 * 1024), b''): h.update(data)
    return h.hexdigest()

if mode == 'inspect':
    repo = request('GET', API)
    releases = request('GET', API + '/releases')
    print(json.dumps({'repo': repo['full_name'], 'private': repo['private'], 'default_branch': repo['default_branch'], 'releases': [{'id': r['id'], 'tag': r['tag_name'], 'draft': r['draft']} for r in releases]}))
elif mode == 'upload':
    head = subprocess.check_output(['git','-c',f'safe.directory={PUBLIC.as_posix()}','-C',str(PUBLIC),'rev-parse','HEAD'], text=True).strip()
    releases = request('GET', API + '/releases')
    release = next((r for r in releases if r['tag_name'] == 'v0.1.0'), None)
    body = (PUBLIC / 'docs/RELEASE-v0.1.0.md').read_text(encoding='utf-8')
    if release is None:
        release = request('POST', API + '/releases', json={'tag_name':'v0.1.0','target_commitish':head,'name':'Dino Attack · Flow RL v0.1.0','body':body,'draft':True,'prerelease':True})
    if not release['draft']: raise RuntimeError('Release is already public; inspect instead of changing it.')
    assert release['target_commitish'] == head
    for path in sorted(ART.iterdir()):
        if not path.is_file() or path.suffix not in ('.zip','.json','.txt'): continue
        existing = next((a for a in release['assets'] if a['name'] == path.name), None)
        if existing:
            assert existing.get('digest') == 'sha256:' + digest(path), path.name
            continue
        with path.open('rb') as data:
            asset = request('POST', release['upload_url'].split('{')[0], params={'name':path.name}, content=data,
                            headers={'Content-Type':'application/octet-stream', 'Content-Length':str(path.stat().st_size)})
        assert asset['size'] == path.stat().st_size
        if asset.get('digest'): assert asset['digest'] == 'sha256:' + digest(path)
        print('Uploaded and verified:', path.name, flush=True)
    (ROOT / 'docs/release/v010-draft.json').write_text(json.dumps({'id':release['id'],'head':head,'url':release['html_url']}, indent=2))
elif mode == 'retarget':
    state = json.loads((ROOT / 'docs/release/v010-draft.json').read_text())
    r = request('GET', API + '/releases/' + str(state['id']))
    assert r['draft'], 'Cannot retarget a published release.'
    head = subprocess.check_output(['git','-c',f'safe.directory={PUBLIC.as_posix()}','-C',str(PUBLIC),'rev-parse','HEAD'], text=True).strip()
    r = request('PATCH', API + '/releases/' + str(state['id']), json={'target_commitish':head})
    assert r['target_commitish'] == head
    state['head'] = head
    (ROOT / 'docs/release/v010-draft.json').write_text(json.dumps(state, indent=2))
    print('Draft source commit:', head)
elif mode == 'tag':
    state = json.loads((ROOT / 'docs/release/v010-draft.json').read_text())
    response = session.get(API + '/git/ref/tags/v0.1.0', timeout=60)
    if response.status_code == 404:
        request('POST', API + '/git/refs', json={'ref':'refs/tags/v0.1.0','sha':state['head']})
    else:
        response.raise_for_status()
        assert response.json()['object']['sha'] == state['head']
    r = request('PATCH', API + '/releases/' + str(state['id']), json={'tag_name':'v0.1.0','target_commitish':state['head']})
    assert r['tag_name'] == 'v0.1.0' and not r['draft']
    print(json.dumps({'url':r['html_url'],'tag':r['tag_name'],'assets':len(r['assets'])}))
elif mode == 'publish':
    state = json.loads((ROOT / 'docs/release/v010-draft.json').read_text())
    r = request('GET', API + '/releases/' + str(state['id']))
    expected = {p.name for p in ART.iterdir() if p.is_file() and p.suffix in ('.zip','.json','.txt')}
    assert {a['name'] for a in r['assets']} == expected
    for asset in r['assets']:
        assert asset['size'] == (ART / asset['name']).stat().st_size
        if asset.get('digest'): assert asset['digest'] == 'sha256:' + digest(ART / asset['name'])
    r = request('PATCH', API + '/releases/' + str(state['id']), json={'draft':False,'tag_name':'v0.1.0'})
    print(json.dumps({'url':r['html_url'],'draft':r['draft'],'prerelease':r['prerelease'],'assets':len(r['assets'])}))
elif mode == 'verify':
    release_url = 'https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0'
    page = session.get(release_url, timeout=60)
    page.raise_for_status()
    assets = [{'name':p.name, 'browser_download_url':'https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/download/v0.1.0/' + p.name}
              for p in sorted(ART.iterdir()) if p.is_file() and p.suffix in ('.zip','.json','.txt')]
    out = ROOT / 'release-staging/verify-v010-source/.artifact-downloads'
    out.mkdir(exist_ok=True)
    for asset in assets:
        path = out / asset['name']
        if not path.exists():
            with session.stream('GET', asset['browser_download_url'], timeout=600) as response:
                response.raise_for_status()
                with path.open('xb') as f:
                    for data in response.iter_bytes(1024 * 1024): f.write(data)
        assert digest(path) == digest(ART / path.name), path.name
        print('Anonymous download hash PASS:', path.name, flush=True)
    (ROOT / 'docs/release/v010-remote-verification.json').write_text(json.dumps({'url':release_url,'public_page_status':page.status_code,'asset_count':len(assets),'all_download_hashes_match':True,'anonymous_api_note':'Unauthenticated API returned HTTP 403; public page and direct downloads verified instead.'}, indent=2))
else: raise ValueError(mode)
