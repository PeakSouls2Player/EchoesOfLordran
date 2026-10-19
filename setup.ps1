# Echoes of Lordran: one-step developer setup.
# Run from the repo root (expected at C:\EchoesOfLordran):  powershell -ExecutionPolicy Bypass -File setup.ps1
# Fetches the open-source dependencies into tools\, checks the ones you must get yourself, then builds the tools and
# the loader. It never downloads or touches game files; those come from your own DSR and DS2 SotFS installs.
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Tools = Join-Path $Root "tools"
New-Item -ItemType Directory -Force $Tools | Out-Null
if ($Root -ne "C:\EchoesOfLordran") {
    Write-Warning "The scripts and tools expect the repo at C:\EchoesOfLordran (it is at $Root). Move it there, or search-replace that path."
}

function Need($what, $ok, $how) {
    if ($ok) { Write-Host "[ok]      $what" -ForegroundColor Green; return $true }
    Write-Host "[missing] $what  ->  $how" -ForegroundColor Yellow; return $false
}

# 1. git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "git is required: https://git-scm.com/download/win" }

# 2. open-source dependencies, cloned next to src (the project files reference these paths)
$repos = @{
    "SoulsFormatsNEXT" = "https://github.com/soulsmods/SoulsFormatsNEXT"
    "minhook"          = "https://github.com/TsudaKageyu/minhook"
}
foreach ($name in $repos.Keys) {
    $dir = Join-Path $Tools $name
    if (-not (Test-Path $dir)) { Write-Host "cloning $name ..."; git clone --depth 1 $repos[$name] $dir }
    Need "tools\$name" (Test-Path $dir) "git clone $($repos[$name]) tools\$name" | Out-Null
}

# 3. things you install yourself
$dotnet = if (Test-Path "$Tools\dotnet\dotnet.exe") { "$Tools\dotnet\dotnet.exe" } else { (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
$haveDotnet = $dotnet -and ((& $dotnet --list-sdks 2>$null) -match '^10\.')
$llvm = Get-ChildItem $Tools -Directory -Filter "llvm-mingw-*-ucrt-x86_64" -ErrorAction SilentlyContinue | Select-Object -First 1
$ok = $true
$ok = (Need ".NET 10 SDK" $haveDotnet "https://dotnet.microsoft.com/download (or unzip it to tools\dotnet)") -and $ok
$ok = (Need "llvm-mingw (ucrt, x86_64)" $llvm "https://github.com/mstorsjo/llvm-mingw/releases -> unzip to tools\llvm-mingw-<date>-ucrt-x86_64") -and $ok
$ok = (Need "Smithbox (param defs + Pfim.dll)" (Test-Path "$Tools\smithbox\Assets\PARAM") "download Smithbox and unzip it to tools\smithbox") -and $ok
$ok = (Need "UXM Selective Unpack" (Test-Path "$Tools\uxm") "download UXM Selective Unpack to tools\uxm (unpacks your DS2 install)") -and $ok
Need "DARK SOULS II: SotFS (Steam)" (Test-Path "C:\Program Files (x86)\Steam\steamapps\common\Dark Souls II Scholar of the First Sin") "install it from Steam (or set the paths in the tools)" | Out-Null
Write-Host "Also needed: DARK SOULS: REMASTERED installed (the DS1 source data)."

if ($NoBuild) { return }
if (-not $ok) { Write-Host "`nFix the [missing] items above, then run setup.ps1 again." -ForegroundColor Yellow; return }

# 4. build
Write-Host "`nbuilding Lordran.Tools ..."
$env:DOTNET_ROOT = Split-Path $dotnet
& $dotnet build (Join-Path $Root "src\Lordran.Tools") -c Release
Write-Host "`nbuilding the loader ..."
& cmd /c (Join-Path $Root "src\loader\build.bat")
Write-Host "`nDone. Next: unpack DS2 (lr extract-ds2), then build the pack - see README.md." -ForegroundColor Green
