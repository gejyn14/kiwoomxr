#!/usr/bin/env python3
"""Bounded source preflight; not a substitute for Unity compile or headset tests."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
QUEST = ROOT / "quest"
errors = []
manifest = json.loads((QUEST / "Packages/manifest.json").read_text())
expected = {
    "com.meta.xr.sdk.core": "205.0.0",
    "com.meta.xr.sdk.interaction": "205.0.0",
    "com.meta.xr.sdk.interaction.ovr": "205.0.0",
    "com.unity.xr.openxr": "1.18.0",
    "com.unity.xr.management": "4.5.3",
    "com.unity.ugui": "2.0.0",
}
for package, version in expected.items():
    if manifest["dependencies"].get(package) != version:
        errors.append(f"Unexpected version: {package}")
if "m_EditorVersion: 6000.0.67f1\n" not in (QUEST / "ProjectSettings/ProjectVersion.txt").read_text():
    errors.append("Unexpected Unity editor version")
source = QUEST / "Assets/SpatialTrading"
for path in source.rglob("*.asmdef"):
    json.loads(path.read_text())
for path in (source / "Domain").glob("*.cs"):
    text = path.read_text()
    if re.search(r"^using (Unity|Oculus|Meta|System.Net)", text, re.MULTILINE):
        errors.append(f"Domain boundary violation: {path.name}")
# No broker/Gemini/network execution or order actions exist in the M1 runtime.
for path in list((source / "Runtime").rglob("*.cs")) + list((source / "Domain").glob("*.cs")):
    text = path.read_text()
    if re.search(r"UnityWebRequest|HttpClient|ClientWebSocket|DllImport|CONFIRM_ORDER|CREATE_ORDER_DRAFT|OrderExecutor", text):
        errors.append(f"Out-of-milestone runtime operation: {path.name}")
# Check filenames only. Never read, print, or copy private credential files.
private_files = sum(1 for path in (QUEST / "Assets").rglob("*") if path.is_file() and
                    re.search(r"appkey|secretkey|access.?token|\.env$|credentials", path.name, re.IGNORECASE))
if private_files:
    errors.append(f"Private configuration filenames detected under Unity Assets: {private_files}")
if errors:
    raise SystemExit("\n".join(errors))
print("M1 source preflight PASS: version pins, pure domain, no order/network runtime, no private config filenames in Assets.")
print("Scope: static source checks only; Unity/Android/headset behavior is not established.")
