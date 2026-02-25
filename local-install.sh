export DOTNET_EnableWriteXorExecute=0
dotnet pack -o publish
dotnet tool uninstall -g ConsoleToSvg || true
dotnet tool install -g  --add-source ./publish ConsoleToSvg --prerelease
