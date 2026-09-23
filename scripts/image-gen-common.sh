#!/usr/bin/env bash
# Shared setup for image generation scripts (docs site and README).
# This file is meant to be sourced, not executed directly.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

local_console2svg_dir=
if ! command -v console2svg >/dev/null 2>&1; then
  local_console2svg_dir="$(mktemp -d)"
  trap 'rm -rf "$local_console2svg_dir"' EXIT
  find "$repo_root/src" -type d \( -name bin -o -name obj \) -exec rm -rf {} + 2>/dev/null || true
  dotnet publish "$repo_root/src/ConsoleToSvg/ConsoleToSvg.csproj" \
    --configuration Release --output "$local_console2svg_dir" >/dev/null
  export PATH="$local_console2svg_dir:$PATH"
fi

# --- install required packages ---
sudo npm install -g oh-my-logo
sudo apt update
sudo apt install -y software-properties-common
sudo add-apt-repository -y ppa:zhangsongcui3371/fastfetch
sudo apt update
sudo apt install -y librsvg2-bin sl nyancat vim tmux ffmpeg cmatrix btop pipes-sh fastfetch

# --- masking fixture ---
cat <<EOF > .env
CURRENT_DIRECTORY=$(pwd)
APP_SECRET_TOKEN=1234567890thankyou
HTTP_PROXY=http://user:password@10.0.0.1:8080
CONNECTION_STRING=Server=localhost;Database=myDataBase;User Id=myUsername;Password=myPassword;
MYSQL_CONNECTION_URL=mysql://myUsername:myPassword@localhost:3306/myDatabase
GIT_USERNAME=hidden_truth_name
GIT_EMAIL_ADDRESS=hidden_truth_name@example.com
COMMON_HASH=0123456789abcdef0123456789abcdef
SECRET_HASH=fedcba9876543210fedcba9876543210
EOF
