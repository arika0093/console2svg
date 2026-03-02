# --- image ---
# required: sudo npm install -g oh-my-logo
console2svg -o ./assets/cmd-hero.svg      --verbose ./logs/cmd-hero.log       -c -d macos-pc -h 10 --opacity 0.9 --background ./assets/bg.png  -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
console2svg -o ./assets/cmd-hero-grad.svg --verbose ./logs/cmd-hero-grad.log  -c -d macos-pc -h 10 --opacity 0.95 --background "#30a0d0" "#0060c0" -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
console2svg -o ./assets/cmd.svg           --verbose ./logs/cmd.log            console2svg 
console2svg -o ./assets/cmd-window.svg    --verbose ./logs/cmd-window.log     -w 120 -c -d macos-pc  -- console2svg
console2svg -o ./assets/cmd-crop-word.svg --verbose ./logs/cmd-crop-word.log  --crop-top "Host" --crop-bottom ".NET runtimes installed:-2" -- dotnet --info
## background
console2svg -o ./assets/cmd-bg1.svg       --verbose ./logs/cmd-bg1.log        -h 10 -c -d macos-pc --background "#003060" --opacity 0.8  -- dotnet --version
console2svg -o ./assets/cmd-bg2.svg       --verbose ./logs/cmd-bg2.log        -h 10 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.8  -- dotnet --version
console2svg -o ./assets/cmd-bg3.svg       --verbose ./logs/cmd-bg3.log        -h 10 -c -d macos-pc --background ./assets/bg.png --opacity 0.8   -- dotnet --version
## window chrome
console2svg -o ./assets/window/none.svg       -d none       -w 40 -h 4 -c -- dotnet --version
console2svg -o ./assets/window/macos.svg      -d macos      -w 40 -h 4 -c -- dotnet --version
console2svg -o ./assets/window/macos-pc.svg   -d macos-pc   -w 40 -h 4 -c -- dotnet --version
console2svg -o ./assets/window/windows.svg    -d windows    -w 40 -h 4 -c -- dotnet --version
console2svg -o ./assets/window/windows-pc.svg -d windows-pc -w 40 -h 4 -c -- dotnet --version
# --- video ---
# required: sudo apt install -y sl htop
console2svg -o ./assets/cmd-sl.svg        --verbose ./logs/cmd-sl.log         -w 120 -h 16 -c -d -v -- sl
console2svg -o ./assets/cmd-htop.svg      --verbose ./logs/cmd-htop.log       -w 120 -h 20 -c -d -v --timeout 8 --sleep 0 -- htop -d 10
# required: sudo apt install -y vim
console2svg -o ./assets/cmd-bash-vim.svg  --verbose ./logs/cmd-bash-vim.log   -w 80 -h 20 -v -d --replay ./assets/cmd-bash-vim-replay.json -- bash
# required: sudo npm install -g @github/copilot
# console2svg -o ./assets/cmd-loop.svg      --verbose ./logs/cmd-loop.log       -v -c -d  --replay ./assets/cmd-loop-replay.json -- copilot --banner
