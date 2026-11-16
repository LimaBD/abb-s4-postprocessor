#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
test_project="$repo_root/postprocessor/tests/AbbS4Postprocessor.Tests/AbbS4Postprocessor.Tests.csproj"

dotnet test "$test_project"
