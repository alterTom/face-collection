$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishParent = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'publish'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $publishParent 'windows-x64'))

if ([System.IO.Path]::GetDirectoryName($publishDirectory) -ne $publishParent -or
    [System.IO.Path]::GetFileName($publishDirectory) -ne 'windows-x64') {
    throw "Unsafe publish directory: $publishDirectory"
}

function Assert-LastExitCode([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step failed with exit code $LASTEXITCODE."
    }
}

Push-Location $projectRoot
try {
    dotnet restore 'FaceCaptureAgent\FaceCaptureAgent.csproj' `
        --configfile 'NuGet.Config' `
        -r win-x64 `
        -p:SelfContained=true
    Assert-LastExitCode 'Application restore'
    dotnet restore 'FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj' --configfile 'NuGet.Config'
    Assert-LastExitCode 'Test restore'
    dotnet test 'FaceCaptureAgent.Tests\FaceCaptureAgent.Tests.csproj' -c Release --no-restore
    Assert-LastExitCode 'Release tests'
    if (Test-Path -LiteralPath $publishDirectory) {
        $resolvedPublishDirectory = (Resolve-Path -LiteralPath $publishDirectory).Path
        if (-not $resolvedPublishDirectory.Equals(
                $publishDirectory,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Publish directory resolved outside the expected path: $resolvedPublishDirectory"
        }
        Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
    }
    dotnet publish 'FaceCaptureAgent\FaceCaptureAgent.csproj' `
        -c Release `
        -r win-x64 `
        --self-contained true `
        --no-restore `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publishDirectory
    Assert-LastExitCode 'Windows x64 publish'
    Copy-Item 'config.toml' (Join-Path $publishDirectory 'config.toml') -Force
    Write-Host "Published: $publishDirectory"
}
finally {
    Pop-Location
}
