$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    foreach ($project in @('tests\CoreChecks\CoreChecks.csproj','tests\ScriptChecks\ScriptChecks.csproj','tests\TextSenderStudio.Vision.Checks\TextSenderStudio.Vision.Checks.csproj','tests\InputBridge.Checks\InputBridge.Checks.csproj','tests\TextSenderStudio.Input.Checks\TextSenderStudio.Input.Checks.csproj')) {
        & dotnet run --project $project -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
    }
} finally { Pop-Location }
