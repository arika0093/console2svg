# ConsoleToSvg — 設計計画

## 概要

コマンドの実行結果を SVG として出力する dotnet global tool。
`my-command | console2svg` によるパイプ入力と、`console2svg --command "..."` による PTY 実行の両方をサポートする。
生成物は CSS アニメーション付き SVG（動画モード）または静止 SVG（画像モード）で、README 等に直接貼り付けられる自己完結ファイルとして出力される。

```sh
# パイプモード（pipe stdin）
my-command --color=always | console2svg --out output.svg

# PTY モード（色を保持したままコマンド実行）
console2svg --command "git log --oneline" --out output.svg

# 動画モード
console2svg --command "cargo build" --out output.svg --mode video

# クロップ付き画像モード
console2svg --command "dotnet --info" --out output.svg --crop-top 2ch --crop-bottom 10px
```

---

## プロジェクト構成

単一のメインプロジェクト + テストプロジェクトの 2 プロジェクト構成。

```
ConsoleToSvg/
├── LICENSE
├── PLAN.md
├── version.json                    # Nerdbank.GitVersioning 設定
├── src/
│   ├── Directory.Build.props       # ライブラリ共通設定（netstandard2.0 ベース）
│   ├── README.md                   # NuGet パッケージ README
│   └── ConsoleToSvg/
│       ├── ConsoleToSvg.csproj     # ツール本体
│       ├── Program.cs              # エントリポイント・CLI 定義
│       ├── Recording/
│       │   ├── AsciicastWriter.cs  # asciicast v2 NDJSON 書き込み
│       │   ├── AsciicastReader.cs  # asciicast v2 NDJSON 読み込み
│       │   ├── PipeRecorder.cs     # stdin パイプ読み取り
│       │   └── PtyRecorder.cs      # PTY 経由コマンド実行・録画
│       ├── Terminal/
│       │   ├── AnsiParser.cs       # ANSI/VT100 エスケープシーケンス解析
│       │   ├── ScreenBuffer.cs     # 2D セル配列（char + FG + BG + 装飾）
│       │   └── TerminalEmulator.cs # イベントストリーム → ScreenBuffer 変換
│       └── Svg/
│           ├── SvgRenderer.cs      # 静止 SVG 生成
│           ├── AnimatedSvgRenderer.cs # CSS アニメーション SVG 生成
│           └── CropOptions.cs      # クロップパラメータモデル
└── tests/
    ├── Directory.Build.props
    └── ConsoleToSvg.Tests/
        ├── ConsoleToSvg.Tests.csproj
        ├── Terminal/
        │   ├── AnsiParserTests.cs
        │   └── ScreenBufferTests.cs
        └── Svg/
            ├── SvgRendererTests.cs
            └── AnimatedSvgRendererTests.cs
```

---

## ビルド設定

### `src/ConsoleToSvg/ConsoleToSvg.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- Directory.Build.props の netstandard2.0 を上書き -->
    <TargetFrameworks>netcoreapp3.0;net8.0;net9.0;net10.0</TargetFrameworks>
    <OutputType>Exe</OutputType>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>console2svg</ToolCommandName>
    <!-- EOL TFM 警告を抑制 (netcoreapp3.0 は EOL だが意図的サポート) -->
    <CheckEolTargetFramework>false</CheckEolTargetFramework>
    <RollForward>LatestMajor</RollForward>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.0.3" />
    <PackageReference Include="Quick.PtyNet" Version="1.0.3" />
    <!-- Quick.PtyNet.WinPty は Quick.PtyNet の hard dependency として自動取得 -->
  </ItemGroup>
</Project>
```

### `dotnet tool install` 時の TFM 選択

`dotnet pack` 時に全 TFM のバイナリが `tools/<tfm>/any/` に格納される。
`dotnet tool install` はインストール先のランタイムに最も近い TFM を自動選択する。

| インストール先ランタイム | 使用される TFM |
|---|---|
| .NET 10 | `net10.0` |
| .NET 9 | `net9.0` |
| .NET 8 | `net8.0` |
| .NET 5 / 6 / 7 / Core 3.x | `netcoreapp3.0` |

---

## 依存ライブラリ

| ライブラリ | バージョン | 用途 |
|---|---|---|
| `System.CommandLine` | 2.0.3 | CLI オプション定義・パース（netstandard2.0 互換） |
| `Quick.PtyNet` | 1.0.3 | PTY 実行（netstandard2.0、Windows ConPTY / WinPTY 自動選択） |
| `Quick.PtyNet.WinPty` | 1.0.1 | WinPTY ネイティブバイナリ（Quick.PtyNet の hard dependency） |
| `Nerdbank.GitVersioning` | 3.* | バージョン管理（version.json から自動生成） |
| `Polyfill` | 9.9.0 | 新 BCL API を旧 TFM にバックポート |
| `SonarAnalyzer.CSharp` | 10.* | 静的解析（analyzer-only） |
| `TUnit` | 1.* | テストフレームワーク（tests のみ） |
| `Shouldly` | 4.* | アサーションライブラリ（tests のみ） |

---

## 入力モード

### パイプモード (`Console.IsInputRedirected == true`)

- `Console.OpenStandardInput()` でバイト列を読み取る
- 各チャンク受信時に `DateTimeOffset.UtcNow` から経過時間をタイムスタンプとして記録
- ANSI エスケープコードは通常 pipe 時に無効化されるため、`--color=always` 等を README で案内する

```sh
# 色を保持するには --color=always や FORCE_COLOR=1 を使用
git log --oneline --color=always | console2svg --out output.svg
FORCE_COLOR=1 npx eslint . | console2svg --out output.svg
```

### PTY モード (`--command "..."`)

- `Quick.PtyNet` の `PtyProvider.SpawnAsync(PtyOptions, CancellationToken)` で PTY プロセスを生成
- `IPtyConnection.ReaderStream` からバイト列 + タイムスタンプを収集
- `PtyOptions.Cols` / `Rows` は `--width` / `--height` オプションで指定（デフォルト: 80×24）
- Windows: build 17763+ は ConPTY、それ以前は WinPTY（自動判別）
- Unix: `forkpty` P/Invoke

### 共通出力: asciicast v2 イベントストリーム

両モードとも同一の内部形式（asciicast v2 NDJSON）に変換する。

```
{"version": 2, "width": 80, "height": 24, "timestamp": 1234567890}
[0.123, "o", "\u001b[32mhello\u001b[0m\r\n"]
[0.456, "o", "world\r\n"]
```

`--save-cast <path>` を指定すると中間 `.cast` ファイルとして保存できる。

---

## VT100 / ANSI パーサー

### 対応シーケンス

| カテゴリ | シーケンス |
|---|---|
| SGR 色 | 8色 (`ESC[30-37m`, `ESC[40-47m`)、256色 (`ESC[38;5;Nm`)、Truecolor (`ESC[38;2;R;G;Bm`) |
| SGR 装飾 | Bold (`ESC[1m`)、Italic (`ESC[3m`)、Underline (`ESC[4m`)、Reset (`ESC[0m`) |
| カーソル移動 | `ESC[A/B/C/D`（相対）、`ESC[H` / `ESC[f`（絶対）、`ESC[s` / `ESC[u`（保存/復元） |
| 消去 | `ESC[J`（画面消去）、`ESC[K`（行消去）、`ESC[2J`（全消去） |
| スクロール領域 | `ESC[r`（スクロール領域設定） |
| オルタネートスクリーン | `ESC[?1049h` / `ESC[?1049l` |
| OSC | タイトル設定（無視） |

### スクリーンバッファ

```
ScreenBuffer
├── int Width, Height
├── Cell[,] Cells              // [row, col]
├── (int Row, int Col) Cursor
└── struct Cell
    ├── char Character
    ├── Color Foreground        // R/G/B または 8色インデックス
    ├── Color Background
    ├── bool Bold
    ├── bool Italic
    └── bool Underline
```

---

## SVG レンダラー

### 静止 SVG（`--mode image`、デフォルト）

1. asciicast イベントストリームを最初から最後まで TerminalEmulator に流す
2. 最終状態の `ScreenBuffer` を取得（`--frame N` で N フレーム前の状態も選択可）
3. クロップ処理を適用した後、SVG を生成

**SVG 構造:**
```xml
<svg viewBox="0 0 W H" xmlns="...">
  <style>
    .bg { font-family: monospace; font-size: 14px; }
    /* テーマカラー変数 */
  </style>
  <rect width="W" height="H" fill="背景色"/>
  <!-- 各行ごとに <text> または <tspan> で文字列を出力 -->
  <!-- 色ブロックは <rect> で背景色を描画 -->
</svg>
```

### アニメーション SVG（`--mode video`）

1. 各 asciicast イベント（タイムスタンプ付き出力チャンク）を 1 フレームとして処理
2. 各フレームで TerminalEmulator を進め、ScreenBuffer スナップショットを取得
3. 各フレームを `<g id="frame-N" style="visibility:hidden">` として描画
4. CSS `@keyframes` でタイムスタンプに合わせて visibility を制御

```css
@keyframes anim {
  0.00% { visibility: hidden }
  5.00% { visibility: visible }
  /* ... */
}
#frame-1 { animation: anim 10s steps(1) infinite; }
```

### クロップ処理

`--crop-top`・`--crop-right`・`--crop-bottom`・`--crop-left` の 4 オプションで各辺を独立指定。

**単位:**
- `px`: SVG の `viewBox` 調整 + `<clipPath>` で切り抜き（文字の途中も切れる）
- `ch`: 文字数単位でバッファを切り出し（行・列単位の整数切り捨て）

**混在例:**
```sh
console2svg --command "..." --out out.svg \
  --crop-top 1ch \
  --crop-right 5px \
  --crop-bottom 20px \
  --crop-left 0
```

**`ch` 単位の適用範囲:**
- 水平（`--crop-left`・`--crop-right`）: 列単位でバッファを切り出し
- 垂直（`--crop-top`・`--crop-bottom`）: 行単位でバッファを切り出し

---

## CLI オプション一覧

| オプション | 型 | デフォルト | 説明 |
|---|---|---|---|
| `--command` / `-c` | string | — | PTY モードで実行するコマンド |
| `--out` / `-o` | string | `output.svg` | 出力ファイルパス |
| `--mode` | `image`\|`video` | `image` | 出力モード |
| `--width` | int | `80` | 端末幅（文字数） |
| `--height` | int | `24` | 端末高（行数） |
| `--frame` | int | 最終フレーム | 静止画に使用するフレーム番号（`image` モード） |
| `--crop-top` | string | `0` | 上辺クロップ量（`px` または `ch`） |
| `--crop-right` | string | `0` | 右辺クロップ量（`px` または `ch`） |
| `--crop-bottom` | string | `0` | 下辺クロップ量（`px` または `ch`） |
| `--crop-left` | string | `0` | 左辺クロップ量（`px` または `ch`） |
| `--theme` | string | `dark` | カラーテーマ（`dark`\|`light`\|カスタム JSON） |
| `--save-cast` | string | — | asciicast v2 中間ファイルの保存先パス |
| `--in` | string | — | 既存の `.cast` ファイルを入力として使用 |

---

## テスト方針

テストプロジェクト: `tests/ConsoleToSvg.Tests/`

- **TFM**: `net10.0`（`tests/Directory.Build.props` に設定済み）
- **フレームワーク**: TUnit 1.* + Shouldly 4.*
- **テスト対象**:
  - `AnsiParser`: 各エスケープシーケンスの正確な解析
  - `ScreenBuffer`: カーソル移動・消去・スクロールの状態遷移
  - `SvgRenderer`: 既知の ScreenBuffer に対する SVG 出力の検証
  - `AnimatedSvgRenderer`: フレーム数・タイミングの検証
  - 統合テスト: `echo` / `dir` / `cat` 等の実コマンドを通じた end-to-end 検証

---

## 実装ロードマップ

1. `ConsoleToSvg.csproj` 作成・ビルド確認（マルチ TFM）
2. `Terminal/` 層実装 → 単体テスト作成
3. `Recording/` 層実装（pipe モード → PTY モード の順）
4. `Svg/` 層実装（静止画 → アニメーションの順）
5. `Program.cs` CLI 定義・統合
6. 統合テスト・README 作成
