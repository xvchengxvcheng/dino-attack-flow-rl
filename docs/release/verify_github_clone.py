"""Verify the freshly cloned source publication without running Unity or training."""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys

root = Path(sys.argv[1]).resolve()
manifest = json.loads((root / "source-manifest.json").read_text(encoding="utf-8"))
for item in manifest["files"]:
    path = root / item["path"]
    assert path.is_relative_to(root), item["path"]
    data = path.read_bytes()
    assert len(data) == item["bytes"], item["path"]
    assert hashlib.sha256(data).hexdigest() == item["sha256"], item["path"]

links = 0
for path in [root / "README.md", *sorted((root / "docs").glob("*.md"))]:
    text = path.read_text(encoding="utf-8-sig")
    for url in re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", text):
        if url.startswith(("https://", "http://")):
            continue
        if url.startswith("#"):
            assert f'id="{url[1:]}"' in text, (path, url)
        else:
            assert (path.parent / url.split("#")[0]).exists(), (path, url)
        links += 1

commit = subprocess.check_output(
    ["git", "-c", f"safe.directory={root.as_posix()}", "-C", str(root), "rev-parse", "HEAD"],
    text=True,
).strip()
print(json.dumps({
    "commit": commit,
    "verified_files": len(manifest["files"]),
    "verified_local_links": links,
    "status": "PASS",
}, ensure_ascii=False))
