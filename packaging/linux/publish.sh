#!/usr/bin/env bash
# Publishes the self-contained app (and the askpass helper) for one runtime identifier.
#
#   packaging/linux/publish.sh [linux-x64|linux-arm64] [output-dir]
#
# Cross publishing linux-arm64 from an x64 machine works (nothing is compiled natively).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

RID="${1:-linux-x64}"
OUT="${2:-$repo_root/artifacts/publish/$RID}"
rm -rf "$OUT"
dotnet publish "$repo_root/src/app/GitExtensions.Avalonia/GitExtensions.Avalonia.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:DebugType=None -p:DebugSymbols=false \
  -o "$OUT"
check_publish_dir "$OUT"
echo "Published $RID to $OUT"
