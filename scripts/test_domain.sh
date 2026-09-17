#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
mkdir -p "$ROOT/artifacts/domain"
RUN_DIR="$(mktemp -d "$ROOT/artifacts/domain/test-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")"
"$DOTNET" test "$ROOT/tests/Domain/Domain.Tests.csproj" \
  --logger 'trx;LogFileName=domain.trx' \
  --results-directory "$RUN_DIR"
