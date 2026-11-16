$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "postprocessor\tests\AbbS4Postprocessor.Tests\AbbS4Postprocessor.Tests.csproj"

dotnet test $testProject
