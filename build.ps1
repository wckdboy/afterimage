param([string]$DotNet = 'dotnet', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Afterimage.csproj'
if (-not $NoRestore) {
    & $DotNet restore $project --configfile (Join-Path $PSScriptRoot 'nuget.config') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
}
$publish = Join-Path $PSScriptRoot 'artifacts/app'
& $DotNet publish $project -c Release --no-restore --self-contained false -p:DebugType=None -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close Afterimage before rebuilding.' }
Get-ChildItem -LiteralPath $publish -File | Copy-Item -Destination $PSScriptRoot -Force
Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Afterimage.exe') | Select-Object Name, Length
