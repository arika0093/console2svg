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

# --- image ---
console2svg capture -o ./assets/cmd-hero.svg        --verbose ./logs/cmd-hero.log         -w 100 -h 10 -c -d macos-pc --opacity 0.95 --background ./assets/image1.png -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
console2svg capture -o ./assets/cmd-btop.svg        --verbose ./logs/cmd-btop.log         -w 150 -h 32 -d macos-pc --opacity 0.95 --background "#30a0d0" "#0060c0" --timeout 3 -- btop -u 100
console2svg capture -o ./assets/cmd.svg             --verbose ./logs/cmd.log              -w 120 -- console2svg
console2svg capture -o ./assets/cmd-window.svg      --verbose ./logs/cmd-window.log       -w 100 -c -d macos-pc  -- fastfetch
console2svg capture -o ./assets/cmd-crop-word.svg   --verbose ./logs/cmd-crop-word.log    -w 100 --crop-top "Host" --crop-bottom ".NET runtimes installed:-2" -- dotnet --info
console2svg capture -o ./assets/cmd-term-custom.svg --verbose ./logs/cmd-term-custom.log  -w 100 -h 4 --prompt "[HELLO!] $" --header "my-custom-header" --forecolor "#00f040" --backcolor "#042515" -- echo "hi"
## background
console2svg capture -o ./assets/cmd-bg1.svg       --verbose ./logs/cmd-bg1.log  -w 100 -h 10 -c -d macos-pc --background "#003060" --opacity 0.85 -- dotnet --version
console2svg capture -o ./assets/cmd-bg2.svg       --verbose ./logs/cmd-bg2.log  -w 100 -h 10 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.85 -- dotnet --version
console2svg capture -o ./assets/cmd-bg3.svg       --verbose ./logs/cmd-bg3.log  -w 100 -h 10 -c -d macos-pc --background ./assets/image2.png --opacity 0.85 -- dotnet --version
## window chrome
console2svg capture -o ./assets/window/none.svg        -d none        -w 40 -h 4 -c -- dotnet --version
console2svg capture -o ./assets/window/macos.svg       -d macos       -w 40 -h 4 -c -- dotnet --version
console2svg capture -o ./assets/window/macos-pc.svg    -d macos-pc    -w 40 -h 4 -c -- dotnet --version
console2svg capture -o ./assets/window/windows.svg     -d windows     -w 40 -h 4 -c -- dotnet --version
console2svg capture -o ./assets/window/windows-pc.svg  -d windows-pc  -w 40 -h 4 -c -- dotnet --version
console2svg capture -o ./assets/window/transparent.svg -d transparent -w 40 -h 4 -c -- dotnet --version

# --- video ---
console2svg capture -o ./assets/cmd-sl.svg            --verbose ./logs/cmd-sl.log           -w 120 -h 16 -c -d -v -- sl
console2svg capture -o ./assets/cmd-nyancat.svg       --verbose ./logs/cmd-nyancat.log      -w 160 -h 28 -c -d -v --timeout 5 --sleep 0.5 -- nyancat
console2svg capture -o ./assets/cmd-loop.svg          --verbose ./logs/cmd-loop.log         -w 80 -h 20 -v -d windows --timeout 10 -- /usr/games/pipes -t 0 -f 35
# console2svg replay ./assets/cmd-bash-vim-replay.json -o ./assets/cmd-bash-vim.svg --verbose ./logs/cmd-bash-vim.log -w 80 -h 20 -v -d -- bash
bash ./scripts/demos/generate-interactive.sh
bash ./scripts/demos/generate-tmux.sh

# --- video(gif) ---
console2svg capture -o ./assets/cmd-matrix-video.gif -w 100 -h 24 -v -c -d macos-pc --timeout 5 --fps 30 -- cmatrix -ab
