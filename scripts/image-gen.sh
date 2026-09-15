#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
assets_dir="$repo_root/docs/public/assets"
mkdir -p "$assets_dir/window"
mkdir -p "$assets_dir/theme"
mkdir -p "$repo_root/logs"

# --- install require packages ---
sudo npm install -g oh-my-logo
sudo apt update
sudo apt install -y software-properties-common
sudo add-apt-repository -y ppa:zhangsongcui3371/fastfetch
sudo apt update
sudo apt install -y librsvg2-bin sl nyancat vim tmux ffmpeg cmatrix btop pipes-sh fastfetch

# --- image ---
console2svg capture -o "$assets_dir/cmd-hero.svg"        --verbose ./logs/cmd-hero.log         -w 100 -h 10 -c -d macos-pc --opacity 0.95 --background "$assets_dir/image1.png" -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
console2svg capture -o "$assets_dir/cmd-btop.svg"        --verbose ./logs/cmd-btop.log         -w 150 -h 32 -d macos-pc --opacity 0.95 --background "#30a0d0" "#0060c0" --timeout 3 -- btop -u 100
console2svg capture -o "$assets_dir/cmd.svg"             --verbose ./logs/cmd.log              -w 120 -- console2svg
console2svg capture -o "$assets_dir/cmd-window.svg"      --verbose ./logs/cmd-window.log       -w 100 -c -d macos-pc  -- fastfetch
console2svg capture -o "$assets_dir/cmd-crop-word.svg"   --verbose ./logs/cmd-crop-word.log    -w 100 --crop-top "Host" --crop-bottom ".NET runtimes installed:-2" -- dotnet --info
console2svg capture -o "$assets_dir/cmd-term-custom.svg" --verbose ./logs/cmd-term-custom.log  -w 100 -h 4 --prompt "[HELLO!] $" --header "my-custom-header" --forecolor "#00f040" --backcolor "#042515" -- echo "hi"
## background
console2svg capture -o "$assets_dir/cmd-bg1.svg"       --verbose ./logs/cmd-bg1.log  -w 100 -h 10 -c -d macos-pc --background "#003060" --opacity 0.85 -- dotnet --version
console2svg capture -o "$assets_dir/cmd-bg2.svg"       --verbose ./logs/cmd-bg2.log  -w 100 -h 10 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.85 -- dotnet --version
console2svg capture -o "$assets_dir/cmd-bg3.svg"       --verbose ./logs/cmd-bg3.log  -w 100 -h 10 -c -d macos-pc --background "$assets_dir/image2.png" --opacity 0.85 -- dotnet --version
## window chrome
console2svg capture -o "$assets_dir/window/none.svg"        -d none        -w 40 -h 4 -c -- dotnet --version
console2svg capture -o "$assets_dir/window/macos.svg"       -d macos       -w 40 -h 4 -c -- dotnet --version
console2svg capture -o "$assets_dir/window/macos-pc.svg"    -d macos-pc    -w 40 -h 4 -c -- dotnet --version
console2svg capture -o "$assets_dir/window/windows.svg"     -d windows     -w 40 -h 4 -c -- dotnet --version
console2svg capture -o "$assets_dir/window/windows-pc.svg"  -d windows-pc  -w 40 -h 4 -c -- dotnet --version
console2svg capture -o "$assets_dir/window/transparent.svg" -d transparent -w 40 -h 4 -c -- dotnet --version

## theme (same framing as window/ outputs, one file per bundled --theme id)
# window/ covers chrome via -d; theme/ covers every --theme id (palettes, chrome variants, full themes).
THEMES="dark light dracula github-dark github-light gruvbox-dark gruvbox-light matrix nord one-light solarized-dark solarized-light tokyo-night none transparent macos macos-pc windows windows-pc cyberpunk cyberpunk-pc"
for theme in $THEMES; do
  console2svg capture -o "$assets_dir/theme/${theme}.svg" --verbose "./logs/theme-${theme}.log" -w 40 -h 4 -c --theme "$theme" -- dotnet --version
done

## theme + docs examples (used by gallery.mdx / quick-start.mdx / themes.md)
console2svg capture -o "$assets_dir/cmd-theme-dracula.svg"      --verbose ./logs/cmd-theme-dracula.log      -w 100 -c --theme dracula --theme macos -- fastfetch
console2svg capture -o "$assets_dir/cmd-theme-cyberpunk-pc.svg" --verbose ./logs/cmd-theme-cyberpunk-pc.log -w 100 -c --theme cyberpunk-pc -- fastfetch
console2svg capture -o "$assets_dir/cmd-quickstart-fastfetch.svg" --verbose ./logs/cmd-quickstart-fastfetch.log -w 100 -c --theme github-dark --theme macos-pc --background "#006090" -- fastfetch
# static cmatrix frame for gallery (video is cmd-matrix-video.gif)
console2svg capture -o "$assets_dir/cmd-cmatrix.svg" --verbose ./logs/cmd-cmatrix.log -w 100 -h 24 -c -d macos-pc --timeout 2 -- cmatrix -ab
# static PNG next to the animated GIF (converting-output-formats.md)
console2svg capture -o "$assets_dir/cmd-matrix.png" --verbose ./logs/cmd-matrix-png.log -w 100 -h 24 -c -d macos-pc --timeout 2 -- cmatrix -ab
# before/after masking example with synthetic credentials only (masking-sensitive-output.md, gallery.mdx)
cat <<'EOF' > /tmp/console2svg-mask-demo.env
CURRENT_DIRECTORY=/home/demo/app
APP_SECRET_TOKEN=demo-token-12345
HTTP_PROXY=http://demo-user:demo-password-12345@10.0.0.1:8080
CONNECTION_STRING=Server=localhost;Database=demoDb;User Id=demoUser;Password=demo-Password-12345;
GIT_USERNAME=demo-user
GIT_EMAIL_ADDRESS=demo-user@example.com
EOF
console2svg capture -o "$assets_dir/cmd-mask-before.svg" --verbose ./logs/cmd-mask-before.log -w 100 -d macos-pc -- cat /tmp/console2svg-mask-demo.env
console2svg capture -o "$assets_dir/cmd-mask-after.svg"  --verbose ./logs/cmd-mask-after.log  -w 100 -d macos-pc --mask "demo-token-12345" "demo-password-12345" "demo-Password-12345" "demo-user" "demo-user@example.com" -- cat /tmp/console2svg-mask-demo.env

# --- video ---
console2svg capture -o "$assets_dir/cmd-sl.svg"            --verbose ./logs/cmd-sl.log           -w 120 -h 16 -c -d -v -- sl
console2svg capture -o "$assets_dir/cmd-nyancat.svg"       --verbose ./logs/cmd-nyancat.log      -w 160 -h 28 -c -d -v --timeout 5 --sleep 0.5 -- nyancat
console2svg capture -o "$assets_dir/cmd-loop.svg"          --verbose ./logs/cmd-loop.log         -w 40 -h 10 -v -d windows --timeout 10 -- /usr/games/pipes -t 0 -f 35
bash ./scripts/demos/generate-interactive.sh
bash ./scripts/demos/generate-tmux.sh

# --- video(gif) ---
console2svg capture -o "$assets_dir/cmd-matrix-video.gif" -w 100 -h 24 -v -c -d macos-pc --timeout 5 --fps 30 -- cmatrix -ab
