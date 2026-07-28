#!/usr/bin/env bash
set -euo pipefail

converter_package="$(find ./nuget-artifacts -maxdepth 1 -name 'ConsoleToSvg.Converter.*.nupkg' -printf '%f\n')"
converter_version="${converter_package#ConsoleToSvg.Converter.}"
converter_version="${converter_version%.nupkg}"

cat > ./nuget-artifacts/NuGet.Config <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="." />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="ConsoleToSvg.Converter" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

dotnet restore src/ConsoleToSvg/ConsoleToSvg.csproj \
  --force \
  --configfile "$GITHUB_WORKSPACE/nuget-artifacts/NuGet.Config" \
  -p:UseConverterPackage=true \
  -p:ConverterPackageVersion="$converter_version"
echo "version=$converter_version" >> "$GITHUB_OUTPUT"
