param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$props = Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw
$version = $props.SelectSingleNode('//Version').InnerText.Trim()

$outputDir = Join-Path $root "publish\$Runtime"
dotnet publish (Join-Path $root 'src\PCPulse.App\PCPulse.App.csproj') `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:Version=$version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "Ошибка публикации PCPulse ($LASTEXITCODE)."
}

Write-Host "Published PCPulse v$version to $outputDir"
