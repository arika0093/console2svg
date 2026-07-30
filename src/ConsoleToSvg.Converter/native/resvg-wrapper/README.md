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

In CI, source `dotnet build` or `dotnet publish` invokes Cargo and copies the library beside
the application output. Local builds leave this disabled by default; install Rust and set
`BuildResvgNative=true` when a local build needs to bundle the library.

The release workflow collects all six release RID builds before packaging
`ConsoleToSvg.Converter`. It places each generated library in the NuGet conventional
`runtimes/<rid>/native` directory; generated libraries are not committed.
