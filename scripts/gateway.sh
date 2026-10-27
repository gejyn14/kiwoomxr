#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
exec env -u KIWOOM_APPKEY -u KIWOOM_SECRETKEY -u DART_API_KEY uv run --project gateway --env-file gateway/.env uvicorn spatial_gateway.app:app --app-dir gateway/src --host "${GATEWAY_HOST:-127.0.0.1}" --port "${GATEWAY_PORT:-8471}" --no-access-log
