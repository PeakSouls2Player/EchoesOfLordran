param([string]$Add = "", [switch]$Clear, [switch]$Show)
# Test variants on top of the installed pack (session 5 bisect): -Add DIR copies DIR's tree (e.g. gamedata\param\X.param)
# into data\lordran\pack and records every copied file in pack\variant.txt; -Clear deletes exactly those files again, so
# the default pack is back without a rebuild. Refuses to overwrite a file the pack already has (clearing would then
# delete a default-pack file) and refuses while DS2 runs (it serves files from the pack).
$pack = "C:\EchoesOfLordran\data\lordran\pack"
$list = Join-Path $pack "variant.txt"
if ($Show) { if (Test-Path $list) { Get-Content $list } else { "no variant installed" }; exit 0 }
if (Get-Process DarkSoulsII -ErrorAction SilentlyContinue) { "DS2 is running: not changing the pack"; exit 1 }
if ($Clear) {
    if (-not (Test-Path $list)) { "no variant installed"; exit 0 }
    foreach ($rel in Get-Content $list) { $f = Join-Path $pack $rel; if (Test-Path $f) { Remove-Item $f -Force; "removed $rel" } }
    Remove-Item $list -Force
    # drop folders the variant created and left empty
    Get-ChildItem $pack -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem $_.FullName -Force) } | ForEach-Object { Remove-Item $_.FullName; "removed empty $($_.FullName.Substring($pack.Length + 1))" }
    exit 0
}
if ($Add) {
    if (Test-Path $list) { "a variant is already installed (pack-variant.ps1 -Clear first):"; Get-Content $list; exit 1 }
    $src = (Resolve-Path $Add).Path
    $files = Get-ChildItem $src -Recurse -File
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($src.Length + 1)
        if (Test-Path (Join-Path $pack $rel)) { "refusing: the pack already has $rel"; exit 1 }
    }
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($src.Length + 1)
        $to = Join-Path $pack $rel
        New-Item -ItemType Directory -Force (Split-Path $to -Parent) | Out-Null
        Copy-Item $f.FullName $to
        Add-Content -Path $list -Value $rel
        "added $rel ($($f.Length) bytes)"
    }
    exit 0
}
"usage: pack-variant.ps1 -Add DIR | -Clear | -Show"
exit 1
