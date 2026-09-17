#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
"$DOTNET" test "$ROOT/tests/Domain/Domain.Tests.csproj" \
  --logger 'trx;LogFileName=domain.trx' \
  --results-directory "$ROOT/artifacts/milestone1/domain"
