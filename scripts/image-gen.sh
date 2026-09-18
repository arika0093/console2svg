#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

# --- install require packages ---
sudo npm install -g oh-my-logo
sudo apt update
sudo apt install -y software-properties-common
sudo add-apt-repository -y ppa:zhangsongcui3371/fastfetch
sudo apt update
sudo apt install -y librsvg2-bin sl nyancat vim tmux ffmpeg cmatrix btop pipes-sh fastfetch

# --- README images ---
console2svg batch markdown -i ./README.md -o ./assets --verbose

# These demos drive interactive terminal sessions and are not plain command captures.
bash ./scripts/demos/generate-interactive.sh
bash ./scripts/demos/generate-tmux.sh
