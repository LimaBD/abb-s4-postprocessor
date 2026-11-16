$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "postprocessor\ABB_S4_SprutCAMX.csproj"

dotnet build $project
