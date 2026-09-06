[CmdletBinding()]
param(
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $PSScriptRoot '..\installer-output\刷脸认证.exe'
}

function Assert-Condition([bool]$condition, [string]$message) {
    if (-not $condition) {
        throw $message
    }
}

function Get-OptionalRegistryValue([string]$path, [string]$name) {
    $item = Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }

    $property = $item.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-PeSubsystem([string]$path) {
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4 + 20
        $optionalHeaderMagic = $reader.ReadUInt16()
        Assert-Condition ($optionalHeaderMagic -in 0x10b, 0x20b) 'Invalid PE optional header.'
        $stream.Position += 66
        return $reader.ReadUInt16()
    }
    finally {
        $stream.Dispose()
    }
}

$resolvedInstaller = Resolve-Path -LiteralPath $InstallerPath -ErrorAction SilentlyContinue
Assert-Condition ($null -ne $resolvedInstaller) "Installer package not found: $InstallerPath"

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'FaceCaptureAgent'
$existingRunValue = Get-OptionalRegistryValue $runKey $runValueName
Assert-Condition ([string]::IsNullOrEmpty($existingRunValue)) `
    'FaceCaptureAgent is already registered for startup; uninstall it before running this test.'

$uninstallRoot = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
$existingProduct = Get-ChildItem -LiteralPath $uninstallRoot -ErrorAction SilentlyContinue |
    ForEach-Object { Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue } |
    Where-Object {
        $displayName = $_.PSObject.Properties['DisplayName']
        $displayName -and $displayName.Value -in @('FaceCaptureAgent', '刷脸认证')
    } |
    Select-Object -First 1
Assert-Condition ($null -eq $existingProduct) `
    'FaceCaptureAgent is already registered as an installed product; uninstall it before running this test.'

$existingProcess = Get-CimInstance Win32_Process -Filter "Name = 'FaceCaptureAgent.exe'" -ErrorAction SilentlyContinue
Assert-Condition ($null -eq $existingProcess) `
    'FaceCaptureAgent.exe is already running; stop it before running this test.'

$testId = [Guid]::NewGuid().ToString('N')
$testDirectory = Join-Path $env:LOCALAPPDATA "Temp\FaceCaptureAgentInstallerTest-$testId"
$unrelatedDirectory = Join-Path $env:LOCALAPPDATA "Temp\FaceCaptureAgentUnrelated-$testId"
$installLog = Join-Path $env:TEMP "FaceCaptureAgentInstallerTest-$testId.log"
$installedExecutable = Join-Path $testDirectory 'FaceCaptureAgent.exe'
$unrelatedExecutable = Join-Path $unrelatedDirectory 'FaceCaptureAgent.exe'
$uninstaller = Join-Path $testDirectory 'unins000.exe'
$installedProcess = $null
$unrelatedProcess = $null
$uninstallerStoppedProcess = $false
$unrelatedProcessSurvived = $false
$uninstallerRemovedStartup = $false
$uninstallerRemovedDirectory = $false
$uninstallExitCode = $null
$remainingProcess = $null
$remainingRunValue = $null
$remainingDirectory = $true

try {
    Write-Host "Installing test package to: $testDirectory"
    $installerProcess = Start-Process -FilePath $resolvedInstaller.Path `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$testDirectory", "/LOG=$installLog") `
        -PassThru
    $installerProcess.WaitForExit()
    Assert-Condition ($installerProcess.ExitCode -eq 0) `
        "Installer exited with code $($installerProcess.ExitCode)."
    Write-Host 'Installer process completed.'
    Assert-Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) `
        'Installer did not create FaceCaptureAgent.exe.'
    Assert-Condition (Test-Path -LiteralPath (Join-Path $testDirectory 'config.toml') -PathType Leaf) `
        'Installer did not create config.toml.'
    Assert-Condition ((Get-PeSubsystem $installedExecutable) -eq 2) `
        'FaceCaptureAgent.exe is not a Windows GUI application and may show a console window.'
    Write-Host 'Installed files and GUI subsystem verified.'

    $configPath = Join-Path $testDirectory 'config.toml'
    $configMarker = "# installer-preserve-marker-$testId"
    Add-Content -LiteralPath $configPath -Value $configMarker
    Write-Host 'Running an in-place upgrade to verify configuration preservation.'
    $upgradeProcess = Start-Process -FilePath $resolvedInstaller.Path `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$testDirectory", "/LOG=$installLog") `
        -PassThru
    $upgradeProcess.WaitForExit()
    Assert-Condition ($upgradeProcess.ExitCode -eq 0) `
        "Upgrade installer exited with code $($upgradeProcess.ExitCode)."
    Assert-Condition (Select-String -LiteralPath $configPath -SimpleMatch $configMarker -Quiet) `
        'An in-place upgrade overwrote the existing config.toml.'
    Write-Host 'Existing configuration survived the in-place upgrade.'

    $expectedRunValue = '"' + $installedExecutable + '"'
    $actualRunValue = Get-ItemPropertyValue -LiteralPath $runKey -Name $runValueName
    Assert-Condition ($actualRunValue -eq $expectedRunValue) `
        "Unexpected startup command: $actualRunValue"
    Write-Host 'Current-user startup registration verified.'

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    Write-Host 'Waiting for the installed background process.'
    do {
        Start-Sleep -Milliseconds 250
        $installedProcess = Get-CimInstance Win32_Process -Filter "Name = 'FaceCaptureAgent.exe'" `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.ExecutablePath -eq $installedExecutable } |
            Select-Object -First 1
    } while ($null -eq $installedProcess -and [DateTime]::UtcNow -lt $deadline)

    Assert-Condition ($null -ne $installedProcess) 'Installed background process did not start.'
    Write-Host "Background process started with PID $($installedProcess.ProcessId)."

    $listenerDeadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        Start-Sleep -Milliseconds 250
        $listener = Get-NetTCPConnection -LocalAddress '127.0.0.1' -LocalPort 17653 -State Listen `
            -ErrorAction SilentlyContinue |
            Where-Object { $_.OwningProcess -eq $installedProcess.ProcessId }
    } while ($null -eq $listener -and [DateTime]::UtcNow -lt $listenerDeadline)
    Assert-Condition ($null -ne $listener) 'Installed process is not listening on 127.0.0.1:17653.'
    Write-Host 'Loopback listener verified.'

    New-Item -ItemType Directory -Path $unrelatedDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') `
        -Destination $unrelatedExecutable
    $unrelatedProcess = Start-Process -FilePath $unrelatedExecutable `
        -ArgumentList @('-t', '127.0.0.1') `
        -WindowStyle Hidden `
        -PassThru
    Start-Sleep -Milliseconds 500
    Assert-Condition ($null -ne (Get-Process -Id $unrelatedProcess.Id -ErrorAction SilentlyContinue)) `
        'Could not start the unrelated same-name process used by the uninstall safety test.'
    Write-Host "Unrelated same-name process started with PID $($unrelatedProcess.Id)."
}
finally {
    if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
        Write-Host 'Uninstalling while the temporary background process is running.'
        $uninstallProcess = Start-Process -FilePath $uninstaller `
            -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
            -PassThru
        $uninstallProcess.WaitForExit()
        $uninstallExitCode = $uninstallProcess.ExitCode
        Write-Host 'Uninstaller process completed.'

        $cleanupDeadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            if ($null -ne $installedProcess) {
                $remainingProcess = Get-Process -Id $installedProcess.ProcessId -ErrorAction SilentlyContinue
            }
            $remainingRunValue = Get-OptionalRegistryValue $runKey $runValueName
            $remainingDirectory = Test-Path -LiteralPath $testDirectory
            if (($null -eq $remainingProcess) -and
                [string]::IsNullOrEmpty($remainingRunValue) -and
                -not $remainingDirectory) {
                break
            }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $cleanupDeadline)

        $uninstallerStoppedProcess = $null -eq $remainingProcess
        $uninstallerRemovedStartup = [string]::IsNullOrEmpty($remainingRunValue)
        $uninstallerRemovedDirectory = -not $remainingDirectory
        if ($null -ne $unrelatedProcess) {
            $unrelatedProcessSurvived = $null -ne (
                Get-Process -Id $unrelatedProcess.Id -ErrorAction SilentlyContinue)
        }
    }

    if ($null -ne $installedProcess -and -not $uninstallerStoppedProcess) {
        Write-Warning 'Cleaning up a background process that survived uninstall.'
        Stop-Process -Id $installedProcess.ProcessId -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $installedProcess.ProcessId -Timeout 5 -ErrorAction SilentlyContinue
    }

    if ($null -ne $unrelatedProcess) {
        Stop-Process -Id $unrelatedProcess.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $unrelatedProcess.Id -Timeout 5 -ErrorAction SilentlyContinue
    }

    $currentRunValue = Get-OptionalRegistryValue $runKey $runValueName
    if ($currentRunValue -and $currentRunValue.Contains($testDirectory)) {
        Remove-ItemProperty -LiteralPath $runKey -Name $runValueName -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $testDirectory) {
        $resolvedTestDirectory = [System.IO.Path]::GetFullPath($testDirectory)
        $expectedParent = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Temp'))
        if ($resolvedTestDirectory.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path -LiteralPath $unrelatedDirectory) {
        $resolvedUnrelatedDirectory = [System.IO.Path]::GetFullPath($unrelatedDirectory)
        $expectedUnrelatedParent = [System.IO.Path]::GetFullPath(
            (Join-Path $env:LOCALAPPDATA 'Temp\FaceCaptureAgentUnrelated-'))
        if ($resolvedUnrelatedDirectory.StartsWith(
                $expectedUnrelatedParent,
                [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedUnrelatedDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Remove-Item -LiteralPath $installLog -Force -ErrorAction SilentlyContinue
}

Assert-Condition ($uninstallExitCode -eq 0) "Uninstaller exited with code $uninstallExitCode."
Assert-Condition $uninstallerStoppedProcess 'Uninstaller did not stop the background process.'
Assert-Condition $unrelatedProcessSurvived 'Uninstaller stopped an unrelated same-name process.'
Assert-Condition $uninstallerRemovedStartup 'Uninstaller did not remove the startup registry value.'
Assert-Condition $uninstallerRemovedDirectory 'Uninstaller did not remove the application directory.'

Write-Host 'Installer integration test passed.'
