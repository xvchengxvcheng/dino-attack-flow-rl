param(
    [string[]]$Name = @('dino-attack-windows-x64-v0.1.0.zip'),
    [string]$DestinationRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$CacheDirectory = '',
    [switch]$All
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$index = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts.json') | ConvertFrom-Json
$selected = @($index.release_attachments | Where-Object { $All -or $Name -contains $_.name })
if (-not $All -and $selected.Count -ne $Name.Count) { throw 'Unknown or duplicate artifact name.' }
$destination = [IO.Path]::GetFullPath($DestinationRoot)
if (-not $CacheDirectory) { $CacheDirectory = Join-Path $destination '.artifact-downloads' }
[IO.Directory]::CreateDirectory($CacheDirectory) | Out-Null
[IO.Directory]::CreateDirectory($destination) | Out-Null
$prefix = $destination.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($asset in $selected) {
    $zipPath = Join-Path $CacheDirectory $asset.name
    if (-not (Test-Path -LiteralPath $zipPath)) {
        $temporary = $zipPath + '.' + [guid]::NewGuid().ToString('N') + '.partial'
        Invoke-WebRequest -UseBasicParsing -Uri $asset.url -OutFile $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Downloaded hash mismatch: $($asset.name)" }
        Move-Item -LiteralPath $temporary -Destination $zipPath
    }
    if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ne $asset.sha256) { throw "Cached hash mismatch: $zipPath" }
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $manifestEntry = $archive.GetEntry('artifact-manifests/' + $asset.name + '.json')
        if (-not $manifestEntry) { throw 'Missing file manifest.' }
        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        # Check every destination before writing; existing user files are never overwritten.
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
            if (-not $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive path.' }
            if (Test-Path -LiteralPath $target) {
                $stream = $entry.Open()
                $hash = [Security.Cryptography.SHA256]::Create()
                try { $expected = ([BitConverter]::ToString($hash.ComputeHash($stream))).Replace('-', '') }
                finally { $stream.Dispose(); $hash.Dispose() }
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw "Existing file differs; choose a new DestinationRoot: $target" }
            }
        }
        foreach ($entry in $archive.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
            if (-not (Test-Path -LiteralPath $target)) {
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $false)
            }
        }
        foreach ($file in $manifest.files) {
            $target = Join-Path $destination $file.path
            if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.sha256) { throw "Extracted hash mismatch: $target" }
        }
    } finally { $archive.Dispose() }
    Write-Output "Verified and installed: $($asset.name)"
}
