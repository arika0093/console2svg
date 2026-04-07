#!/usr/bin/env bash
set -euo pipefail

: "${RELEASE_UPLOAD_DIR:?}"
: "${NATIVE_ARTIFACTS_DIR:?}"
: "${PACKAGE_VERSION:?}"
: "${PACKAGE_ITERATION:?}"
: "${GITHUB_REPOSITORY:?}"

mkdir -p "$RELEASE_UPLOAD_DIR"
release_upload_dir="$(cd "$RELEASE_UPLOAD_DIR" && pwd)"

require_file() {
  local path="$1"
  if [[ ! -f "$path" ]]; then
    echo "Required file not found: $path" >&2
    exit 1
  fi
}

binary_name_for_rid() {
  local rid="$1"
  local tool_name="$2"

  if [[ "$rid" == win-* ]]; then
    printf '%s.exe' "$tool_name"
  else
    printf '%s' "$tool_name"
  fi
}

archive_extension_for_rid() {
  local rid="$1"

  if [[ "$rid" == win-* ]]; then
    printf '.zip'
  else
    printf '.tar.gz'
  fi
}

native_runtime_name_for_rid() {
  local rid="$1"

  case "$rid" in
    win-*)
      printf '%s' "resvg_wrapper.dll"
      ;;
    linux-*)
      printf '%s' "libresvg_wrapper.so"
      ;;
    osx-*)
      printf '%s' "libresvg_wrapper.dylib"
      ;;
    *)
      echo "Unsupported RID for native runtime lookup: $rid" >&2
      exit 1
      ;;
  esac
}

console_binary_path() {
  local rid="$1"
  printf '%s/native-%s/%s' \
    "$NATIVE_ARTIFACTS_DIR" \
    "$rid" \
    "$(binary_name_for_rid "$rid" console2svg)"
}

native_runtime_path() {
  local rid="$1"
  local runtime_name
  runtime_name="$(native_runtime_name_for_rid "$rid")"

  local candidate="${NATIVE_ARTIFACTS_DIR}/native-${rid}/${runtime_name}"
  if [[ -f "$candidate" ]]; then
    printf '%s' "$candidate"
    return 0
  fi

  return 1
}

stage_native_runtime_if_available() {
  local rid="$1"
  local staging_dir="$2"
  local runtime_src runtime_name

  if ! runtime_src="$(native_runtime_path "$rid")"; then
    return 1
  fi

  runtime_name="$(native_runtime_name_for_rid "$rid")"
  cp "$runtime_src" "$staging_dir/$runtime_name"
}

create_standard_bundle() {
  local rid="$1"
  local console_src archive_ext archive_path bundle_dir

  console_src="$(console_binary_path "$rid")"
  require_file "$console_src"

  archive_ext="$(archive_extension_for_rid "$rid")"
  archive_path="${release_upload_dir}/console2svg-${rid}${archive_ext}"
  bundle_dir="$(mktemp -d)"

  cp "$console_src" "$bundle_dir/$(binary_name_for_rid "$rid" console2svg)"
  if [[ "$rid" != win-* ]]; then
    chmod 755 "$bundle_dir/$(binary_name_for_rid "$rid" console2svg)"
  fi

  if ! stage_native_runtime_if_available "$rid" "$bundle_dir"; then
    echo "No ResvgSharp native runtime asset for $rid; bundle will contain console2svg only."
  fi

  if [[ "$archive_ext" == ".zip" ]]; then
    (
      cd "$bundle_dir"
      zip -q -r "$archive_path" .
    )
  else
    tar -czf "$archive_path" -C "$bundle_dir" .
  fi

  rm -rf "$bundle_dir"
  echo "Created standard bundle: $archive_path"
}

build_linux_package() {
  local rid="$1"
  local console_src runtime_src asset_arch deb_arch rpm_arch
  local -a common_args package_inputs

  console_src="$(console_binary_path "$rid")"
  require_file "$console_src"

  case "$rid" in
    linux-x64)
      asset_arch="x64"
      deb_arch="amd64"
      rpm_arch="x86_64"
      ;;
    linux-arm64)
      asset_arch="arm64"
      deb_arch="arm64"
      rpm_arch="aarch64"
      ;;
    *)
      echo "Unsupported Linux RID for packaging: $rid" >&2
      exit 1
      ;;
  esac

  chmod 755 "$console_src"
  common_args=(
    -s dir
    -n console2svg
    -v "$PACKAGE_VERSION"
    --iteration "$PACKAGE_ITERATION"
    --license Apache-2.0
    --url "https://github.com/${GITHUB_REPOSITORY}"
    --description "Convert terminal output to SVG images."
  )
  package_inputs=("$console_src=/usr/local/bin/console2svg")

  if runtime_src="$(native_runtime_path "$rid")"; then
    package_inputs+=("$runtime_src=/usr/local/bin/$(native_runtime_name_for_rid "$rid")")
  else
    echo "No ResvgSharp native runtime asset for $rid; Linux package will include console2svg only."
  fi

  fpm "${common_args[@]}" \
    -t deb \
    -a "$deb_arch" \
    -p "${release_upload_dir}/console2svg.${asset_arch}.deb" \
    "${package_inputs[@]}"

  fpm "${common_args[@]}" \
    -t rpm \
    -a "$rpm_arch" \
    -p "${release_upload_dir}/console2svg.${asset_arch}.rpm" \
    "${package_inputs[@]}"

  echo "Created Linux packages for $rid"
}

build_windows_ffmpeg_bundle() {
  local rid="$1"
  local console_src runtime_src ffmpeg_zip_url temp_root ffmpeg_zip ffmpeg_extract
  local top_dir ffmpeg_bin_dir bundle_dir archive_path

  console_src="$(console_binary_path "$rid")"
  require_file "$console_src"

  if ! runtime_src="$(native_runtime_path "$rid")"; then
    echo "No ResvgSharp native runtime asset for $rid; skipping Windows ffmpeg bundle."
    return 0
  fi

  case "$rid" in
    win-x64)
      ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip"
      ;;
    win-arm64)
      ffmpeg_zip_url="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-winarm64-lgpl.zip"
      ;;
    *)
      echo "Unsupported Windows RID for ffmpeg bundle: $rid" >&2
      exit 1
      ;;
  esac

  archive_path="${release_upload_dir}/console2svg-${rid}-ffmpeg.zip"
  temp_root="$(mktemp -d)"
  ffmpeg_zip="${temp_root}/ffmpeg.zip"
  ffmpeg_extract="${temp_root}/ffmpeg"
  bundle_dir="${temp_root}/bundle"

  curl -fsSL -o "$ffmpeg_zip" "$ffmpeg_zip_url"
  unzip -q "$ffmpeg_zip" -d "$ffmpeg_extract"

  top_dir="$(find "$ffmpeg_extract" -mindepth 1 -maxdepth 1 -type d | sort | head -n 1)"
  if [[ -z "$top_dir" ]]; then
    echo "Unable to determine extracted ffmpeg directory for $rid" >&2
    exit 1
  fi

  ffmpeg_bin_dir="${top_dir}/bin"
  require_file "${ffmpeg_bin_dir}/ffmpeg.exe"
  require_file "${top_dir}/LICENSE.txt"

  mkdir -p "$bundle_dir"
  cp "$console_src" "$bundle_dir/console2svg.exe"
  cp "$runtime_src" "$bundle_dir/resvg_wrapper.dll"
  cp "${ffmpeg_bin_dir}/ffmpeg.exe" "$bundle_dir/ffmpeg.exe"
  cp "${top_dir}/LICENSE.txt" "$bundle_dir/ffmpeg-LICENSE.txt"

  (
    cd "$bundle_dir"
    zip -q -r "$archive_path" .
  )

  rm -rf "$temp_root"
  echo "Created Windows ffmpeg bundle: $archive_path"
}

for rid in linux-x64 linux-arm64 win-x64 win-arm64 osx-x64 osx-arm64; do
  if [[ -f "$(console_binary_path "$rid")" ]]; then
    create_standard_bundle "$rid"
  fi
done

for rid in linux-x64 linux-arm64; do
  if [[ -f "$(console_binary_path "$rid")" ]]; then
    build_linux_package "$rid"
  fi
done

for rid in win-x64 win-arm64; do
  if [[ -f "$(console_binary_path "$rid")" ]]; then
    build_windows_ffmpeg_bundle "$rid"
  fi
done
