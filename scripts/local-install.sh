export DOTNET_EnableWriteXorExecute=0
rm -rf ./publish || true
dotnet clean
dotnet build -c Release --no-cache
dotnet pack src/ConsoleToSvg.Converter/ConsoleToSvg.Converter.csproj -c Release -o publish -p:WarningLevel=0
set -- ./publish/ConsoleToSvg.Converter.*.nupkg
if [ "$#" -ne 1 ] || [ ! -f "$1" ]; then
  echo "Expected exactly one ConsoleToSvg.Converter package in ./publish" >&2
  exit 1
fi
converter_package="${1##*/}"
converter_version="${converter_package#ConsoleToSvg.Converter.}"
converter_version="${converter_version%.nupkg}"
cat > ./publish/NuGet.Config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="." />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
dotnet restore src/ConsoleToSvg/ConsoleToSvg.csproj \
  --force \
  --configfile ./publish/NuGet.Config \
  -p:UseConverterPackage=true \
  -p:ConverterPackageVersion="$converter_version"
dotnet build src/ConsoleToSvg/ConsoleToSvg.csproj -c Release --no-restore \
  -p:UseConverterPackage=true \
  -p:ConverterPackageVersion="$converter_version" \
  -p:BuildResvgNative=false
dotnet pack src/ConsoleToSvg/ConsoleToSvg.csproj -c Release --no-build -o publish \
  -p:WarningLevel=0 \
  -p:UseConverterPackage=true \
  -p:ConverterPackageVersion="$converter_version" \
  -p:BuildResvgNative=false
dotnet tool uninstall -g ConsoleToSvg || true
(cd ./publish && dotnet tool install -g ConsoleToSvg --version "$converter_version")
