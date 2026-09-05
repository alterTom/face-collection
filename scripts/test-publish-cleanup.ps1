$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishDirectory = Join-Path $projectRoot 'publish\windows-x64'
$publishScript = Join-Path $PSScriptRoot 'publish-win-x64.ps1'
$sentinel = Join-Path $publishDirectory 'stale-package-sentinel.txt'

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
Set-Content -LiteralPath $sentinel -Value 'This file must not survive a clean publish.'

try {
    & $publishScript
    if ($LASTEXITCODE -ne 0) {
        throw "Publish script exited with code $LASTEXITCODE."
    }

    if (Test-Path -LiteralPath $sentinel) {
        throw 'The publish script retained a stale file from the previous output.'
    }

    Write-Host 'Publish cleanup test passed.'
}
finally {
    Remove-Item -LiteralPath $sentinel -Force -ErrorAction SilentlyContinue
}
