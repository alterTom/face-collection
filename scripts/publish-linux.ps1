param(
    [ValidateSet('linux-arm64', 'linux-x64')][string]$Runtime = 'linux-arm64',
    [string]$NativeLibraryDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishParent = Join-Path $projectRoot 'publish'
$destination = Join-Path $publishParent $Runtime
function Assert-Exit([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "$Step failed: $LASTEXITCODE" }
}
# Windows checkouts must not ship CRLF shell scripts; fail before building.
foreach ($script in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'installer/linux') -Filter '*.sh' -File) {
    if ([IO.File]::ReadAllText($script.FullName).Contains("`r")) {
        throw "Linux shell script requires LF line endings: $($script.FullName). Restore the file using the repository .gitattributes settings."
    }
}
$nativeArguments = @()
if ($Runtime -eq 'linux-arm64' -and -not $NativeLibraryDirectory) {
    throw 'Kylin ARM64 needs a glibc <= 2.31 native build. Run scripts/build-kylin-native.sh on compatible Linux, then pass -NativeLibraryDirectory. Official ARM64 NuGet requires glibc 2.38.'
}
if ($NativeLibraryDirectory) {
    $NativeLibraryDirectory = (Resolve-Path -LiteralPath $NativeLibraryDirectory).Path
    if (-not (Test-Path -LiteralPath (Join-Path $NativeLibraryDirectory 'libOpenCvSharpExtern.so') -PathType Leaf)) {
        throw 'NativeLibraryDirectory must contain libOpenCvSharpExtern.so.'
    }
    python (Join-Path $PSScriptRoot 'check-linux-native.py') $NativeLibraryDirectory --runtime $Runtime --max-glibc 2.31
    Assert-Exit 'Native ABI check'
    $nativeArguments = @("-p:NativeLibraryDirectory=$NativeLibraryDirectory")
}
# Never recursively remove a destination or follow a publish-directory junction.
foreach ($path in @($publishParent, $destination)) {
    if ((Test-Path -LiteralPath $path) -and
        ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Publish path must not be a link: $path"
    }
}
if (Test-Path -LiteralPath $destination) {
    throw "Output already exists: $destination. Move the previous artifact aside before publishing."
}
Push-Location $projectRoot
try {
    dotnet restore FaceCaptureAgent.Linux/FaceCaptureAgent.Linux.csproj --configfile NuGet.Config -r $Runtime -p:RuntimeIdentifier=$Runtime -p:SelfContained=true @nativeArguments
    Assert-Exit 'Linux restore'
    dotnet publish FaceCaptureAgent.Linux/FaceCaptureAgent.Linux.csproj -c Release -r $Runtime --self-contained true --no-restore -p:PublishSingleFile=false -o $destination @nativeArguments
    Assert-Exit 'Linux publish'
    Copy-Item -LiteralPath (Join-Path $projectRoot 'config.toml') -Destination $destination
    foreach ($required in @('FaceCaptureAgent.Linux', 'config.toml', 'libOpenCvSharpExtern.so', 'Models/face_detection_yunet_2023mar.onnx', 'Models/face_mesh_Nx3x192x192.onnx')) {
        $file = Join-Path $destination $required
        if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
            throw "Incomplete Linux publish; missing or empty file: $required"
        }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'installer/linux') -Destination (Join-Path $destination 'installer') -Recurse
    Write-Host "Published directory: $destination (copy the entire directory; no single-file extraction)."
}
finally { Pop-Location }
