#!/usr/bin/env python3
"""Inspect the built M1 APK. Reads only the SDK's ignored local development token."""
import argparse
import hashlib
import json
import re
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--aapt", required=True, help="aapt from the matching Android SDK")
args = parser.parse_args()
apk = ROOT / "quest/Builds/Android/SpatialTrading-Shell.apk"
badging = subprocess.check_output([args.aapt, "dump", "badging", str(apk)], text=True)
manifest = subprocess.check_output(
    [args.aapt, "dump", "xmltree", str(apk), "AndroidManifest.xml"], text=True)
errors = []


def require(condition, message):
    if not condition:
        errors.append(message)


permissions = re.findall(r"^uses-permission: name='([^']+)'", badging, re.MULTILINE)
require(set(permissions) == {
    "android.permission.INTERNET",
    "com.oculus.permission.HAND_TRACKING",
    "com.kiwoomxr.spatialshell.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
}, "Unexpected Android permissions; inspect the merged manifest")
require("package: name='com.kiwoomxr.spatialshell'" in badging, "Wrong package")
require("sdkVersion:'32'" in badging and "targetSdkVersion:'36'" in badging,
        "Android SDK versions differ from this verified build")
require("uses-feature: name='com.oculus.feature.PASSTHROUGH'" in badging,
        "Passthrough must be required")
require("uses-feature-not-required: name='oculus.software.handtracking'" in badging,
        "Hands must be optional for controller fallback")
require('"quest3"' in manifest, "Quest 3 metadata missing")

# The SDK restores its Editor values after the build. Compare those values with
# uncompressed APK members; never print them or read the user's brokerage files.
settings = ROOT / "quest/Assets/Resources/DevAgentSettings.asset"
tokens = []
if settings.exists():
    for field in ("accessToken", "witClientAccessToken"):
        match = re.search(r"^[ \t]*" + field + r":[ \t]*([^\r\n]*)$",
                          settings.read_text(), re.MULTILINE)
        if match and match.group(1).strip():
            tokens.append(match.group(1).strip().encode())

with zipfile.ZipFile(apk) as archive:
    names = archive.namelist()
    abis = sorted({name.split('/')[1] for name in names if name.startswith('lib/')})
    require(abis == ["arm64-v8a"], "APK must contain only ARM64 native code")
    require("lib/arm64-v8a/libil2cpp.so" in names, "IL2CPP library missing")
    require(not any("operator" in name.lower() for name in names),
            "Editor-only Operator artifact found in APK")
    require(not any(re.search(r"appkey|secretkey|\.env(?:$|\.)|credentials\.(json|txt)",
                              name, re.IGNORECASE) for name in names),
            "Private configuration filename found in APK")
    token_hits = 0
    if tokens:
        for name in names:
            content = archive.read(name)
            token_hits += any(token in content for token in tokens)
    require(token_hits == 0, "Known local SDK token found in APK")

print(json.dumps({
    "result": "FAIL" if errors else "PASS",
    "apk_sha256": hashlib.sha256(apk.read_bytes()).hexdigest(),
    "bytes": apk.stat().st_size,
    "package": "com.kiwoomxr.spatialshell",
    "min_sdk": 32,
    "target_sdk": 36,
    "abis": abis,
    "permissions": permissions,
    "known_local_sdk_token_count": len(tokens),
    "known_token_scan": "PASS" if tokens and token_hits == 0 else
                        "FAIL" if token_hits else "NOT_RUN_NO_LOCAL_TOKEN",
    "scope": "Manifest, filenames, architecture, known SDK token bytes; not device acceptance or a complete secret audit",
    "errors": errors,
}, indent=2))
raise SystemExit(bool(errors))
