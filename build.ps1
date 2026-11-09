param([switch]$Firmware)
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'
$appOutput = Join-Path $artifacts 'TextSenderStudio-win-x64'
New-Item -ItemType Directory -Force -Path $appOutput | Out-Null
& dotnet publish (Join-Path $repoRoot 'src\TextSenderStudio\TextSenderStudio.csproj') -c Release -r win-x64 --self-contained true -o $appOutput -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md'),(Join-Path $repoRoot 'LICENSE'),(Join-Path $repoRoot 'THIRD_PARTY.md') -Destination $appOutput -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $appOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'licenses') -Destination $appOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'vendor\EasyCon.Script\NOTICE.md') -Destination (Join-Path $appOutput 'licenses\EasyCon-NOTICE.md') -Force
if (Test-Path -LiteralPath (Join-Path $appOutput 'settings.json')) { throw 'Publish folder contains personal settings. Use a fresh artifacts folder.' }
if (Test-Path -LiteralPath (Join-Path $appOutput 'projects')) { throw 'Publish folder contains user projects. Use a fresh artifacts folder.' }
$archive = Join-Path $artifacts 'TextSenderStudio-v0.1.0-win-x64.zip'
Compress-Archive -Path (Join-Path $appOutput '*') -DestinationPath $archive -Force
if ($Firmware) { & (Join-Path $repoRoot 'firmware\build.ps1'); if ($LASTEXITCODE -ne 0) { throw 'Firmware build failed.' } }
$firmwareOutput = Join-Path $repoRoot 'firmware\binaries'
if (Test-Path -LiteralPath (Join-Path $firmwareOutput 'TextSenderBridge.ino.merged.bin')) {
    $bundle = Join-Path $artifacts 'TextSenderBridge-ESP32S3-16MB'
    New-Item -ItemType Directory -Force -Path $bundle | Out-Null
    Copy-Item -LiteralPath (Join-Path $firmwareOutput 'TextSenderBridge.ino.merged.bin') -Destination $bundle -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\firmware.md') -Destination (Join-Path $bundle 'README.md') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'firmware\NOTICE.md') -Destination $bundle -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'firmware\LICENSE'),(Join-Path $repoRoot 'firmware\TINYUSB-LICENSE.txt'),(Join-Path $repoRoot 'firmware\SWITCH-DESCRIPTOR-LICENSE.txt') -Destination $bundle -Force
    Compress-Archive -Path (Join-Path $bundle '*') -DestinationPath (Join-Path $artifacts 'TextSenderBridge-v0.1.1-ESP32S3-16MB.zip') -Force
}
Write-Host "Published: $archive"
