# Builds the Windows release artifacts: a portable zip and (when Inno Setup's ISCC.exe is found) a setup installer.
#
#   pwsh packaging/windows/build.ps1 [-Version 0.7.0] [-Arch x64|arm64] [-OutDir artifacts/dist] [-SkipInstaller]
#
# Output:
#   <OutDir>/git-extensions-windows-<Version>-<Arch>.zip
#   <OutDir>/git-extensions-<Version>-windows-<Arch>-setup.exe
#
# Both are self-contained (no .NET runtime needed); Git for Windows must be installed separately.
[CmdletBinding()]
param(
    [string]$Version = '0.7.0',
    [ValidateSet('x64', 'arm64')][string]$Arch = 'x64',
    [string]$OutDir,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'artifacts\dist' }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$rid = "win-$Arch"
$publishDir = Join-Path $repoRoot "artifacts\publish\$rid"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

dotnet publish (Join-Path $repoRoot 'src\app\GitExtensions.Avalonia\GitExtensions.Avalonia.csproj') `
    -c Release -r $rid --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false `
    "-p:Version=$Version" "-p:InformationalVersion=$Version" `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

foreach ($f in 'GitExtensions.Avalonia.exe', 'GitExtensions.Avalonia.dll', 'GitExtensions.AskPass.exe', 'libSkiaSharp.dll') {
    if (-not (Test-Path (Join-Path $publishDir $f))) { throw "$publishDir does not look like a published tree (missing $f)" }
}

# Portable zip: one top-level folder, plus the license.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "gitext-$([guid]::NewGuid().ToString('N'))"
$appDir = Join-Path $stage 'GitExtensions'
New-Item -ItemType Directory -Force $appDir | Out-Null
Copy-Item -Recurse -Force (Join-Path $publishDir '*') $appDir
Copy-Item (Join-Path $repoRoot 'LICENSE.md') $appDir
$zip = Join-Path $OutDir "git-extensions-windows-$Version-$Arch.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
Remove-Item -Recurse -Force $stage
Write-Host "Created $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

if ($SkipInstaller) { return }

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    Write-Warning 'Inno Setup (ISCC.exe) not found: skipping the installer. Install it with: choco install innosetup'
    return
}

& $iscc "/DAppVersion=$Version" "/DArch=$Arch" "/DPublishDir=$publishDir" "/DOutDir=$OutDir" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }
$setup = Join-Path $OutDir "git-extensions-$Version-windows-$Arch-setup.exe"
if (-not (Test-Path $setup)) { throw "missing installer: $setup" }
Write-Host "Created $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)"
