export DOTNET_EnableWriteXorExecute=0
rm -rf ./publish || true
dotnet pack -o publish -p:WarningLevel=0
dotnet tool uninstall -g ConsoleToSvg || true
dotnet tool install -g  --add-source ./publish ConsoleToSvg --prerelease 
