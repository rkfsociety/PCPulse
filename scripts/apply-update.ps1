param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$TargetExePath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
$targetDir = Split-Path -Parent $TargetExePath
$backupPath = "$TargetExePath.previous"
$hadBackup = $false

try {
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -ne $proc) {
        $proc.WaitForExit()
    }

    if (!(Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
        throw 'Файл обновления не найден.'
    }

    $fileName = [IO.Path]::GetFileName($PackagePath)
    if ($fileName -like '*Setup*.exe') {
        $installProc = Start-Process -FilePath $PackagePath -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru
        if ($installProc.ExitCode -ne 0) {
            throw "Установщик завершился с кодом $($installProc.ExitCode)."
        }

        $installedExe = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PCPulse\PCPulse.exe'
        $launchExe = if (Test-Path -LiteralPath $installedExe -PathType Leaf) { $installedExe } else { $TargetExePath }
        if (Test-Path -LiteralPath $launchExe -PathType Leaf) {
            Start-Process -FilePath $launchExe | Out-Null
        }
    }
    else {
        [IO.Directory]::CreateDirectory($targetDir) | Out-Null
        if (Test-Path -LiteralPath $TargetExePath -PathType Leaf) {
            if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
                Remove-Item -LiteralPath $backupPath -Force
            }
            Move-Item -LiteralPath $TargetExePath -Destination $backupPath
            $hadBackup = $true
        }

        Move-Item -LiteralPath $PackagePath -Destination $TargetExePath
        Start-Process -FilePath $TargetExePath | Out-Null
        if (Test-Path -LiteralPath $backupPath -PathType Leaf) {
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        }
    }
}
catch {
    $errMsg = $_.Exception.Message
    if ($hadBackup -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
        try {
            if (Test-Path -LiteralPath $TargetExePath -PathType Leaf) {
                Remove-Item -LiteralPath $TargetExePath -Force
            }
            Move-Item -LiteralPath $backupPath -Destination $TargetExePath -Force
        }
        catch {
            $errMsg += "; ошибка отката: $($_.Exception.Message)"
        }
    }

    try {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $LogPath)) | Out-Null
        [IO.File]::AppendAllText($LogPath, "[$([DateTime]::Now.ToString('s'))] [ERROR] [updater] Ошибка обновления PCPulse: $errMsg`r`n", [Text.Encoding]::UTF8)
    }
    catch { }

    if (Test-Path -LiteralPath $TargetExePath -PathType Leaf) {
        try { Start-Process -FilePath $TargetExePath | Out-Null } catch { }
    }
}
finally {
    if (Test-Path -LiteralPath $PackagePath -PathType Leaf) {
        Remove-Item -LiteralPath $PackagePath -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force -ErrorAction SilentlyContinue
}
