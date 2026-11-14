param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$props = Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw
$version = $props.SelectSingleNode('//Version').InnerText.Trim()
if (-not $version -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Некорректная версия в src/Directory.Build.props: $version"
}

if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish-windows.ps1')
}

$exePath = Join-Path $root 'publish\win-x64\PCPulse.exe'
if (-not (Test-Path $exePath)) {
    throw "Не найден собранный файл: $exePath"
}

$compiler = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $compiler)) {
    throw "Компилятор Inno Setup 6 не найден по пути: $compiler"
}

Push-Location (Join-Path $root 'installer')
try {
    & $compiler "/DAppVersion=$version" 'PCPulse.iss'
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC завершился с кодом $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

$installerPath = Join-Path $root 'dist\PCPulse-Setup-x64.exe'
if (-not (Test-Path $installerPath)) {
    throw "Установщик не был создан: $installerPath"
}

Write-Host "Built installer: $installerPath"
