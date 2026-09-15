---
title: CLIリファレンス
description: console2svgの主要コマンドとオプションをまとめます。
---

インストール済みバージョンの正確な引数一覧は`console2svg --help`で確認してください。このページでは、現在のCLI構成とよく使うオプションをまとめます。

## コマンド

| コマンド | 用途 |
| --- | --- |
| `capture` | コマンドをPTY上で実行して描画します。標準入力pipeは受け付けません。 |
| `interactive` | 対話シェルやプログラムを操作しながら、好きなタイミングで保存します。 |
| `replay` | 保存済みキーボード入力をコマンドへ再生してキャプチャします。 |
| `cast` | Asciicast v2ファイルを読み込んで描画します。 |
| `live-server` | インタラクティブ端末をブラウザへライブ表示します。 |
| `tmux capture` | tmux paneをキャプチャします。 |
| `tmux live-server` | tmux paneをブラウザへライブ表示します。 |
| `theme` | テーマを一覧・インストール・更新・削除します。 |
| `status` | 実行環境、変換バックエンド、出力形式などを表示します。 |
| `update` | 更新確認または自己更新を行います。 |
| `completions script` | シェル補完スクリプトを生成します。 |

現在のCLIに`convert`サブコマンドはありません。画像・動画への変換は`-o`の拡張子で指定し、castファイルの入力には`cast`を使います。

## よく使うオプション

出力先は`-o/--out`、SVGを標準出力へ出す場合は`--stdout`です。端末サイズは`-w/--width`と`-h/--height`で指定します。

`--size`は端末サイズではなく、PNG/GIF/MP4などへ変換するときの出力ピクセル寸法です。

動画・タイミング関連には`-v/--video`、`--mode image|video`、`--fps`、`--sleep`、`--timeout`、`--timing`があります。見た目は`-t/--theme`、`-d/--window`、`--background`、`--opacity`、フォント・色・余白系オプションで調整できます。

機密情報には`--mask`と`--mask-auto`、診断用途には`--verbose`や各種`--embed-*`があります。

> [!TIP]
> キャプチャ対象コマンドの前には`--`を付けておくのがおすすめです。たとえば`console2svg capture -w 100 -- git log --oneline`のようにします。

## ヘルプ

```bash
console2svg --help
console2svg capture --help
console2svg replay --help
console2svg cast --help
console2svg live-server --help
console2svg tmux --help
console2svg theme --help
console2svg status --help
console2svg completions script --help
```

## コマンド別の要点

| コマンド | 主な引数・オプション |
| --- | --- |
| `capture` | `-o/--out`, `-w/-h`, `-v`, `--mode`, `--fps`, `--sleep`, `--timeout`, `--crop-*`, `-t/--theme`, `-d/--window`, `--background`, `--mask`, `--stdout` |
| `replay` | replayファイル、再生先コマンド、`capture`と共通の描画・出力オプション |
| `cast` | `.cast`ファイル、`-o/--out`、描画・変換オプション |
| `interactive` | `-o/--out`、見た目のオプション、`F9`/`F10`/`F12` |
| `live-server` | 任意の`port`または`host:port`位置引数、見た目、`--no-resize`、`--save-cast` |
| `tmux capture` | `--target`、`--history [lines]`、`capture`と共通の描画・出力オプション |
| `tmux live-server` | 任意の`host:port`、`--target`、見た目、`--save-cast` |
| `theme list/install/remove/update` | Theme ID、ディレクトリ・アーカイブ・URL |
| `status` | `--json`、`--format json|markdown|table` |
| `update` | `--check`、`-f/--force`、`-y/--yes` |
| `completions script <shell>` | シェル名を指定して補完スクリプトを標準出力へ生成 |
