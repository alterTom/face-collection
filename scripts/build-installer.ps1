[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0',

    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-LastExitCode([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step failed with exit code $LASTEXITCODE."
    }
}

function Find-Iscc([string]$explicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
        return (Resolve-Path -LiteralPath $explicitPath -ErrorAction Stop).Path
    }

    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $programFiles64 = [Environment]::GetFolderPath('ProgramFiles')
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))
    $candidates.Add((Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe'))
    $candidates.Add((Join-Path $programFiles64 'Inno Setup 6\ISCC.exe'))

    $uninstallRoots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    foreach ($root in $uninstallRoots) {
        Get-ItemProperty $root -ErrorAction SilentlyContinue |
            ForEach-Object {
                $displayName = $_.PSObject.Properties['DisplayName']
                $installLocation = $_.PSObject.Properties['InstallLocation']
                if ($displayName -and
                    $displayName.Value -like 'Inno Setup version 6*' -and
                    $installLocation -and
                    -not [string]::IsNullOrWhiteSpace($installLocation.Value)) {
                    $candidates.Add((Join-Path $installLocation.Value 'ISCC.exe'))
                }
            }
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'Inno Setup 6 command-line compiler (ISCC.exe) was not found. Use -IsccPath to specify it.'
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'
$installerScript = Join-Path $projectRoot 'installer\FaceCaptureAgent.iss'
$installerOutput = Join-Path $projectRoot 'installer-output\刷脸认证.exe'
$compiler = Find-Iscc $IsccPath

Push-Location $projectRoot
try {
    & $publishScript
    Assert-LastExitCode 'Windows x64 application publish'

    & $compiler "/DAppVersion=$Version" $installerScript
    Assert-LastExitCode 'Inno Setup compile'

    if (-not (Test-Path -LiteralPath $installerOutput -PathType Leaf)) {
        throw "Installer was not created: $installerOutput"
    }

    Write-Host "Installer: $installerOutput"
}
finally {
    Pop-Location
}
