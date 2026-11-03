param([string]$Version = '0.1.0')

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Version must be a semantic version.' }
$workspace = Split-Path -Parent $PSScriptRoot
$output = Join-Path $workspace "artifacts/publish/$Version/win-x64"
$archive = Join-Path $workspace "artifacts/WaveMute-$Version-win-x64.zip"

dotnet publish (Join-Path $workspace 'src/WaveMute/WaveMute.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Copy-Item -LiteralPath (Join-Path $workspace 'README.md'), (Join-Path $workspace 'LICENSE'), (Join-Path $workspace 'THIRD-PARTY-NOTICES.txt') -Destination $output
Compress-Archive -LiteralPath (Join-Path $output 'WaveMute.exe'), (Join-Path $output 'README.md'), (Join-Path $output 'LICENSE'), (Join-Path $output 'THIRD-PARTY-NOTICES.txt') -DestinationPath $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$archive.sha256" -Value "$hash  $(Split-Path -Leaf $archive)" -Encoding ascii
Write-Output $archive
Write-Output "$archive.sha256"
