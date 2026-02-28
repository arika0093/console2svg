export DOTNET_EnableWriteXorExecute=0
rm -rf ./publish || true
dotnet clean
dotnet build -c Release --no-cache
dotnet pack -o publish -p:WarningLevel=0
dotnet tool uninstall -g ConsoleToSvg || true
dotnet tool install -g ConsoleToSvg --prerelease 
