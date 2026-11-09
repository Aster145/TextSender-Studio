$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
# A fresh staging directory prevents old DLLs or user data entering the output.
$staging = Join-Path $repoRoot ('work\singlefile-build\' + [guid]::NewGuid().ToString('N'))
$appOutput = Join-Path $repoRoot 'artifacts\TextSenderStudio-singlefile-win-x64'
& dotnet publish (Join-Path $repoRoot 'src\TextSenderStudio\TextSenderStudio.csproj') -c Release -r win-x64 --self-contained true -p:PublishProfile=SingleFile -p:DebugType=None -p:DebugSymbols=false -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }
$files = @(Get-ChildItem -LiteralPath $staging -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'TextSenderStudio.exe') {
    throw ('Expected one executable, but publish produced: ' + (($files | ForEach-Object FullName) -join ', '))
}
New-Item -ItemType Directory -Force -Path $appOutput | Out-Null
# Replace only the executable; preserve any projects/settings created beside it.
$executable = Join-Path $appOutput 'TextSenderStudio.exe'
Copy-Item -LiteralPath $files[0].FullName -Destination $executable -Force
Write-Host "Published single file: $executable"
