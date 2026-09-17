#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the pinned Unity 6000.0.67f1 executable.}"
if [[ ! -x "$UNITY_EDITOR" ]]; then
  echo 'UNITY_EDITOR is not executable.' >&2
  exit 2
fi
mode="${1:-}"
case "$mode" in
  import|configure|scene|validate|test|build) ;;
  *) echo 'Usage: scripts/unity.sh import|configure|scene|validate|test|build' >&2; exit 2 ;;
esac
python3 "$ROOT/scripts/verify_m1_sources.py"
mkdir -p "$ROOT/artifacts/milestone1/unity"
RUN_DIR="$(mktemp -d "$ROOT/artifacts/milestone1/unity/${mode}-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")"
echo "Unity evidence: $RUN_DIR"
args=(-batchmode -projectPath "$ROOT/quest" -logFile "$RUN_DIR/editor.log")
case "$mode" in
  import) args+=(-quit) ;;
  configure) args+=(-quit -executeMethod SpatialTrading.Editor.QuestShellProject.Configure) ;;
  scene) args+=(-executeMethod SpatialTrading.Editor.QuestShellProject.GenerateScene) ;;
  validate) args+=(-quit -executeMethod SpatialTrading.Editor.QuestShellProject.Validate) ;;
  test) args+=(-runTests -testPlatform EditMode -testFilter SpatialTrading.Tests -testResults "$RUN_DIR/editmode.xml") ;;
  build) args+=(-quit -buildTarget Android -executeMethod SpatialTrading.Editor.QuestShellProject.Build) ;;
esac
set +e
"$UNITY_EDITOR" "${args[@]}"
result=$?
set -e
printf '%s\n' "$result" > "$RUN_DIR/exit-code.txt"
if [[ "$result" -ne 0 ]]; then exit "$result"; fi
if [[ "$mode" == build ]]; then
  test -s "$ROOT/quest/Builds/Android/SpatialTrading-Shell.apk"
  shasum -a 256 "$ROOT/quest/Builds/Android/SpatialTrading-Shell.apk" > "$RUN_DIR/apk.sha256"
fi
