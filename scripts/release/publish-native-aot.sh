#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <runtime-identifier>" >&2
  exit 64
fi

rid="$1"
# Windows CI intermittently fails with:
#   CSC : error CS2012: Cannot open '...obj\...\*.dll' for writing
#   -- The process cannot access the file ... because it is being used
#   by another process.; file may be locked by 'VBCSCompiler'.
# The persistent compiler server (VBCSCompiler) holds the intermediate
# assembly while a parallel MSBuild node tries to overwrite it.
# Disable shared compilation and persistent build servers, and serialize
# MSBuild nodes so concurrent CSC invocations cannot race on obj outputs.
dotnet build-server shutdown || true
dotnet publish src/ConsoleToSvg/ConsoleToSvg.csproj \
  -c Release \
  -r "$rid" \
  --self-contained \
  --disable-build-servers \
  -m:1 \
  -p:UseSharedCompilation=false \
  -p:RestoreDisableParallel=true \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  -p:BuildResvgNative=false \
  -p:WarningLevel=0 \
  -o "./native-publish/${rid}"
cp "./native-build/${rid}"/* "./native-publish/${rid}/"
