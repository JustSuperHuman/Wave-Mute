param(
    [string]$Version = '0.1.1',
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Version must be a semantic version.' }
$workspace = Split-Path -Parent $PSScriptRoot
$output = Join-Path $workspace "artifacts/publish/$Version/win-x64"
$archive = Join-Path $workspace "artifacts/WaveMute-$Version-win-x64.zip"
$installer = Join-Path $workspace "artifacts/WaveMute-$Version-Setup.exe"

if (-not $InnoSetupCompiler) {
    $compilerCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerCandidates = @(
        $compilerCommand.Source
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $InnoSetupCompiler = $compilerCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
}
if (-not $InnoSetupCompiler -or -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 is required. Install it with: winget install --id JRSoftware.InnoSetup -e'
}

dotnet publish (Join-Path $workspace 'src/WaveMute/WaveMute.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Copy-Item -LiteralPath (Join-Path $workspace 'README.md'), (Join-Path $workspace 'LICENSE'), (Join-Path $workspace 'THIRD-PARTY-NOTICES.txt') -Destination $output
Compress-Archive -LiteralPath (Join-Path $output 'WaveMute.exe'), (Join-Path $output 'README.md'), (Join-Path $output 'LICENSE'), (Join-Path $output 'THIRD-PARTY-NOTICES.txt') -DestinationPath $archive -Force

$numericVersion = $Version.Split('-')[0] + '.0'
& $InnoSetupCompiler /Qp "/DAppVersion=$Version" "/DAppNumericVersion=$numericVersion" "/DPublishDir=$output" (Join-Path $workspace 'installer/WaveMute.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }

foreach ($asset in @($installer, $archive)) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$asset.sha256" -Value "$hash  $(Split-Path -Leaf $asset)" -Encoding ascii
    Write-Output $asset
    Write-Output "$asset.sha256"
}
