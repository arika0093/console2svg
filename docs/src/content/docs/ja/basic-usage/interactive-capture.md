---
title: インタラクティブキャプチャ
description: 普通にターミナルを操作しながら、好きなタイミングで静止画や動画を保存します。
---

`interactive`は通常のシェルをPTY上で起動し、そのまま手元のターミナルへ転送します。普段どおり作業しながら、欲しい瞬間だけスクリーンショットや録画を保存できます。

```bash
console2svg interactive -d macos -o captures/output.svg
```

特定のプログラムを最初から開きたい場合は、`--`の後ろに指定します。

```bash
console2svg interactive -d macos -- vim README.md
```

操作中に使えるキーは次のとおりです。

- `F10`: 現在の画面を静止画として保存
- `F9`: アニメーション録画を開始 / 停止
- `F12`: 録画中の一時停止 / 再開
- `Ctrl+D`またはシェルの`exit`: セッションを終了

これらのキー入力はconsole2svg側で処理され、シェルには送られません。録画を一時停止している間の出力と経過時間は録画対象から除外されます。

`-o captures/output.svg`のように指定した場合、実際の保存ファイル名にはタイムスタンプが付きます。たとえば`captures/output_20260916_123456789.svg`のような形です。連続して撮影しても同じファイルを上書きしません。

同じキーボード入力をあとから再現したい場合は[録画・リプレイ・castファイル](/ja/advanced-usage/recording-replay-and-cast-files/)を参照してください。

![インタラクティブキャプチャの例](/assets/cmd-interactive.svg)
