#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <rust-target> <output-directory>" >&2
  exit 64
fi

rust_target="$1"
output_directory="$2"
repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repository_root"
manifest="src/ConsoleToSvg.Converter/native/resvg-wrapper/Cargo.toml"

case "$(uname -s)" in
  Darwin) library="libconsole2svg_resvg.dylib" ;;
  MINGW*|MSYS*|CYGWIN*) library="console2svg_resvg.dll" ;;
  *) library="libconsole2svg_resvg.so" ;;
esac

cargo build --manifest-path "$manifest" --release --target "$rust_target"
mkdir -p "$output_directory"
cp "src/ConsoleToSvg.Converter/native/resvg-wrapper/target/$rust_target/release/$library" \
  "$output_directory/$library"
