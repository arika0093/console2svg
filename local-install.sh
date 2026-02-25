export DOTNET_EnableWriteXorExecute=0
dotnet pack -o publish
dotnet tool install -g  --add-source ./publish ConsoleToSvg --prerelease
