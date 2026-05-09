param(
    [string]$OutDir,
    [string]$MakePri,
    [string]$PriConfig,
    [string]$IndexName
)

$tmp = Join-Path $env:TEMP "pri_build_$IndexName"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory $tmp | Out-Null

Copy-Item (Join-Path $OutDir '*.xbf') $tmp -ErrorAction SilentlyContinue
& $MakePri new /pr $tmp /cf $PriConfig /of (Join-Path $tmp 'resources.pri') /in $IndexName /o 2>&1 | Out-Null

$priFile = Join-Path $tmp 'resources.pri'
if (Test-Path $priFile) {
    Copy-Item $priFile (Join-Path $OutDir 'resources.pri') -Force
}
Remove-Item -Recurse -Force $tmp
