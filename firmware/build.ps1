param([string]$ArduinoCli = 'arduino-cli', [string]$CoreVersion = '3.3.8')
$ErrorActionPreference = 'Stop'
if (!(Get-Command $ArduinoCli -ErrorAction SilentlyContinue)) {
    $ideCli = Join-Path $env:LOCALAPPDATA 'Programs\Arduino IDE\resources\app\lib\backend\resources\arduino-cli.exe'
    if (Test-Path -LiteralPath $ideCli) { $ArduinoCli = $ideCli }
    else { throw 'Install arduino-cli or pass -ArduinoCli with its full path.' }
}
$core = Join-Path $env:LOCALAPPDATA ('Arduino15\packages\esp32\hardware\esp32\' + $CoreVersion)
if (!(Test-Path -LiteralPath $core)) { throw "ESP32 core $CoreVersion is required: arduino-cli core install esp32:esp32@$CoreVersion" }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $PSScriptRoot 'binaries'
$build = Join-Path $repo 'work\firmware-build'
New-Item -ItemType Directory -Force -Path $output,$build | Out-Null
$fqbn = 'esp32:esp32:esp32s3:USBMode=default,CDCOnBoot=default,MSCOnBoot=default,DFUOnBoot=default,FlashSize=16M,FlashMode=dio,PSRAM=disabled,PartitionScheme=default'
& $ArduinoCli compile --export-binaries=false --fqbn $fqbn --build-path $build --output-dir $output (Join-Path $PSScriptRoot 'TextSenderBridge')
if ($LASTEXITCODE -ne 0) { throw 'Firmware compile failed.' }
Copy-Item -LiteralPath (Join-Path $core 'tools\partitions\boot_app0.bin') -Destination (Join-Path $output 'boot_app0.bin') -Force
Write-Output "Firmware built in $output"
