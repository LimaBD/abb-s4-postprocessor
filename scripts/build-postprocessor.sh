#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/postprocessor/ABB_S4_SprutCAMX.csproj"

dotnet build "$project"
