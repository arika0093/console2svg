# ConsoleToSvg.Converter resvg wrapper

This crate is the repository-owned native host for `resvg`. `c2s_resvg_warm_system_fonts`
initializes a process-wide `OnceLock<Arc<fontdb::Database>>`; all renders clone that database
instead of rescanning system fonts.

The managed boundary is `../../ResvgNative.cs`. It intentionally exposes only the options used
by ConsoleToSvg: SVG input and optional output width and height. Keep the C ABI and the managed
P/Invoke declarations in sync.

Build a runtime library with:

```bash
./scripts/build-resvg-native.sh <rust-target> <output-directory>
```

A source `dotnet build` or `dotnet publish` with a runtime identifier also invokes Cargo and
copies the library beside the application output. Install Rust and the requested target first.
Set `BuildResvgNative=false` only when a caller supplies the library separately.

The release workflow collects all six release RID builds before packaging
`ConsoleToSvg.Converter`. It places each generated library in the NuGet conventional
`runtimes/<rid>/native` directory; generated libraries are not committed.
