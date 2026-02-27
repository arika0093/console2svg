# required: sudo npm install -g oh-my-logo
console2svg -c -d macos-pc -w 100 -h 10 --opacity 0.9 --background ./assets/bg.png -o ./assets/cmd-hero.svg -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
 console2svg -c -d macos-pc -w 100 -h 10 --opacity 0.95 --background "#30a0d0" "#0060c0" -o ./assets/cmd-hero-grad.svg -- oh-my-logo "console2svg" mint --filled --letter-spacing 0
console2svg console2svg -w 120 -o assets/cmd.svg 
console2svg -w 120 -c -d macos-pc -o ./assets/cmd-window.svg -- console2svg
console2svg --crop-top "Host" --crop-bottom ".NET runtimes installed:-2" -o ./assets/cmd-crop-word.svg -- dotnet --info
console2svg -w 100 -h 10 -c -d macos-pc --background "#003060" --opacity 0.8 -o ./assets/cmd-bg1.svg -- dotnet --version
console2svg -w 100 -h 10 -c -d macos-pc --background "#004060" "#0080c0" --opacity 0.8 -o ./assets/cmd-bg2.svg -- dotnet --version
console2svg -w 100 -h 10 -c -d macos-pc --background ./assets/bg.png --opacity 0.8 -o ./assets/cmd-bg3.svg  -- dotnet --version
# console2svg -v -c -d -h 28 -o ./assets/cmd-loop.svg -- copilot --banner
