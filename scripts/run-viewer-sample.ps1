$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$viewerScript = Join-Path $repoRoot "viewer\rapid_path_viewer.py"
$inputFile = Join-Path $repoRoot "sample\SCXS4JOB.PRG"
$outputHtml = Join-Path $repoRoot "viewer\out\SCXS4JOB_path.html"
$outputReport = Join-Path $repoRoot "viewer\out\SCXS4JOB_report.json"

if (Get-Command py -ErrorAction SilentlyContinue) {
    py -3 $viewerScript $inputFile --out-html $outputHtml --out-report $outputReport
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    python $viewerScript $inputFile --out-html $outputHtml --out-report $outputReport
} else {
    throw "Python launcher not found. Install Python or use 'py'."
}
