#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <runtime-identifier>" >&2
  exit 64
fi

rid="$1"
dotnet publish src/ConsoleToSvg/ConsoleToSvg.csproj \
  -c Release \
  -r "$rid" \
  --self-contained \
  -p:PublishAot=true \
  -p:PublishSingleFile=true \
  -p:BuildResvgNative=false \
  -p:WarningLevel=0 \
  -o "./native-publish/${rid}"
cp "./native-build/${rid}"/* "./native-publish/${rid}/"
