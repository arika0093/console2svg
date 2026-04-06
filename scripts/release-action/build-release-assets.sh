#!/usr/bin/env bash
set -euo pipefail

: "${RELEASE_UPLOAD_DIR:?}"
: "${NATIVE_ARTIFACTS_DIR:?}"
: "${PACKAGE_VERSION:?}"
: "${PACKAGE_ITERATION:?}"
: "${GITHUB_REPOSITORY:?}"

mkdir -p "$RELEASE_UPLOAD_DIR"
release_upload_dir="$(cd "$RELEASE_UPLOAD_DIR" && pwd)"
resvg_cache_dir="$(mktemp -d)"
trap 'rm -rf "$resvg_cache_dir"' EXIT

# Fails fast when a required input file is missing.
require_file() {
  local path="$1"
  if [[ ! -f "$path" ]]; then
    echo "Required file not found: $path" >&2
    exit 1
  fi
}

# Returns the expected console2svg binary name for the target RID.
binary_name_for_rid() {
  local rid="$1"
  local tool_name="$2"

  if [[ "$rid" == win-* ]]; then
    printf '%s.exe' "$tool_name"
  else
    printf '%s' "$tool_name"
  fi
}

# Chooses the archive format we publish for the target RID.
archive_extension_for_rid() {
  local rid="$1"

  if [[ "$rid" == win-* ]]; then
    printf '.zip'
  else
    printf '.tar.gz'
  fi
}

# Maps a RID to the latest upstream resvg release asset.
# Returns non-zero when upstream does not publish a compatible binary.
resvg_asset_for_rid() {
  local rid="$1"

  case "$rid" in
    linux-x64)
      printf '%s' "resvg-linux-x86_64.tar.gz"
      ;;
    linux-arm64)
      return 1
      ;;
    osx-x64)
      printf '%s' "resvg-macos-x86_64.zip"
      ;;
    osx-arm64)
      printf '%s' "resvg-macos-aarch64.zip"
      ;;
    win-x64|win-arm64)
      # Upstream only ships a win64 binary; Windows on ARM can run it via x64 emulation.
      printf '%s' "resvg-win64.zip"
      ;;
    *)
      echo "Unsupported RID for resvg lookup: $rid" >&2
      exit 1
      ;;
  esac
}

# Returns the resvg binary name after extracting the upstream asset.
resvg_binary_name_for_rid() {
  local rid="$1"

  if [[ "$rid" == win-* ]]; then
    printf '%s' "resvg.exe"
  else
    printf '%s' "resvg"
  fi
}

# Resolves the downloaded native console2svg artifact path for a RID.
console_binary_path() {
  local rid="$1"
  printf '%s/native-%s/%s' \
    "$NATIVE_ARTIFACTS_DIR" \
    "$rid" \
    "$(binary_name_for_rid "$rid" console2svg)"
}

# Downloads and caches the latest upstream resvg binary for a RID when available.
ensure_resvg_binary() {
  local rid="$1"
  local asset_name binary_name cache_dir cached_binary temp_dir asset_path extract_dir extracted_binary

  if ! asset_name="$(resvg_asset_for_rid "$rid")"; then
    return 1
  fi

  binary_name="$(resvg_binary_name_for_rid "$rid")"
  cache_dir="${resvg_cache_dir}/${rid}"
  cached_binary="${cache_dir}/${binary_name}"
  if [[ -f "$cached_binary" ]]; then
    printf '%s' "$cached_binary"
    return 0
  fi

  temp_dir="$(mktemp -d)"
  asset_path="${temp_dir}/${asset_name}"
  extract_dir="${temp_dir}/extract"
  mkdir -p "$cache_dir" "$extract_dir"

  curl -fsSL \
    -o "$asset_path" \
    "https://github.com/linebender/resvg/releases/latest/download/${asset_name}"

  if [[ "$asset_name" == *.zip ]]; then
    unzip -q "$asset_path" -d "$extract_dir"
  else
    tar -xzf "$asset_path" -C "$extract_dir"
  fi

  extracted_binary="$(find "$extract_dir" -type f -name "$binary_name" | sort | head -n 1)"
  if [[ -z "$extracted_binary" ]]; then
    echo "Failed to locate $binary_name inside $asset_name" >&2
    find "$extract_dir" -maxdepth 5 -type f | sort >&2
    exit 1
  fi

  cp "$extracted_binary" "$cached_binary"
  if [[ "$binary_name" == "resvg" ]]; then
    chmod 755 "$cached_binary"
  fi

  rm -rf "$temp_dir"
  printf '%s' "$cached_binary"
}

# Copies resvg into a staging directory when upstream provides a compatible binary.
stage_resvg_if_available() {
  local rid="$1"
  local staging_dir="$2"
  local resvg_src

  if ! resvg_src="$(ensure_resvg_binary "$rid")"; then
    return 1
  fi

  cp "$resvg_src" "$staging_dir/$(resvg_binary_name_for_rid "$rid")"
  if [[ "$rid" != win-* ]]; then
    chmod 755 "$staging_dir/$(resvg_binary_name_for_rid "$rid")"
  fi
}

# Creates the standalone tar.gz/zip bundle for a RID.
create_standard_bundle() {
  local rid="$1"
  local console_src archive_ext archive_path bundle_dir

  console_src="$(console_binary_path "$rid")"
  archive_ext="$(archive_extension_for_rid "$rid")"
  archive_path="${release_upload_dir}/console2svg-${rid}${archive_ext}"
  bundle_dir="$(mktemp -d)"

  require_file "$console_src"

  cp "$console_src" "$bundle_dir/$(binary_name_for_rid "$rid" console2svg)"
  if [[ "$rid" != win-* ]]; then
    chmod 755 "$bundle_dir/$(binary_name_for_rid "$rid" console2svg)"
  fi

  if ! stage_resvg_if_available "$rid" "$bundle_dir"; then
    echo "No upstream resvg release asset for $rid; standard bundle will include console2svg only."
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

# Builds the Linux .deb/.rpm packages and adds resvg when upstream publishes one.
build_linux_package() {
  local rid="$1"
  local console_src resvg_src asset_arch deb_arch rpm_arch
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

  if resvg_src="$(ensure_resvg_binary "$rid")"; then
    chmod 755 "$resvg_src"
    package_inputs+=("$resvg_src=/usr/local/bin/resvg")
  else
    echo "No upstream resvg release asset for $rid; Linux package will include console2svg only."
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

# Builds the Windows ffmpeg bundle with resvg, ffmpeg.exe, and the ffmpeg license only.
build_windows_ffmpeg_bundle() {
  local rid="$1"
  local console_src resvg_src ffmpeg_zip_url temp_root ffmpeg_zip ffmpeg_extract
  local top_dir ffmpeg_bin_dir bundle_dir archive_path

  console_src="$(console_binary_path "$rid")"
  require_file "$console_src"

  if ! resvg_src="$(ensure_resvg_binary "$rid")"; then
    echo "Windows ffmpeg bundle requires a resvg binary for $rid" >&2
    exit 1
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
  cp "$resvg_src" "$bundle_dir/resvg.exe"
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
  create_standard_bundle "$rid"
done

for rid in linux-x64 linux-arm64; do
  build_linux_package "$rid"
done

for rid in win-x64 win-arm64; do
  build_windows_ffmpeg_bundle "$rid"
done
