[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$targetPath = [System.IO.Path]::GetFullPath($ExecutablePath)
$processes = Get-Process -Name 'FaceCaptureAgent' -ErrorAction SilentlyContinue
foreach ($process in $processes) {
    try {
        $processPath = $process.Path
    }
    catch {
        continue
    }

    if ([string]::IsNullOrWhiteSpace($processPath)) {
        continue
    }

    $normalizedProcessPath = [System.IO.Path]::GetFullPath($processPath)
    if ($normalizedProcessPath.Equals($targetPath, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Process -Id $process.Id -Force
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
}
