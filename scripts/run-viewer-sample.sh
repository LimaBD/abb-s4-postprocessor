#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
viewer_script="$repo_root/viewer/rapid_path_viewer.py"
input_file="$repo_root/sample/SCXS4JOB.PRG"
output_html="$repo_root/viewer/out/SCXS4JOB_path.html"
output_report="$repo_root/viewer/out/SCXS4JOB_report.json"

python3 "$viewer_script" "$input_file" \
  --out-html "$output_html" \
  --out-report "$output_report"
