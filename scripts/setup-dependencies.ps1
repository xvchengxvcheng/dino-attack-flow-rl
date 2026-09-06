$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dependency = Join-Path $repoRoot 'ml-agents'
$revision = 'a2777719560e4676be99e2ea128c5eb1fbeb3dbb'
if (Test-Path -LiteralPath $dependency) {
    if (-not (Test-Path -LiteralPath (Join-Path $dependency '.git'))) {
        throw "Existing ml-agents directory is not a Git checkout. It was not changed."
    }
    $actual = (& git -C $dependency rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -ne $revision) {
        throw "Existing ml-agents revision does not match $revision. It was not changed."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $dependency 'com.unity.ml-agents/package.json'))) {
        throw 'The expected Unity package is missing. Existing checkout was not changed.'
    }
    Write-Output "ML-Agents already present at $revision"
    exit 0
}
& git clone --filter=blob:none --no-checkout --depth 1 --branch release_23 https://github.com/Unity-Technologies/ml-agents.git $dependency
if ($LASTEXITCODE -ne 0) { throw 'ML-Agents clone failed; partial files are retained for inspection.' }
& git -C $dependency sparse-checkout init --cone
if ($LASTEXITCODE -ne 0) { throw 'Sparse checkout initialization failed.' }
& git -C $dependency sparse-checkout set com.unity.ml-agents
if ($LASTEXITCODE -ne 0) { throw 'Package selection failed.' }
& git -C $dependency fetch --depth 1 origin $revision
if ($LASTEXITCODE -ne 0) { throw 'Pinned revision fetch failed.' }
& git -C $dependency checkout --detach $revision
if ($LASTEXITCODE -ne 0) { throw 'Pinned checkout failed.' }
Write-Output "Ready: $dependency at $revision"
