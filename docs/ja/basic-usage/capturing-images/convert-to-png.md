---
title: PNG形式に変換する
description: 撮影したターミナル出力をPNGラスタ画像へ自動変換する方法とコンバーターの選択。
---

SVG形式に対応していないプラットフォームやアプリケーション向けに、撮影結果をPNG画像として直接出力できます。

## PNG形式で出力する

`-o` オプションで拡張子に `.png` を指定する（または `--format png` を使う）だけで、自動的にラスタライズ処理が行われます。

```bash title="Terminal" "-o output.png"
console2svg capture -o output.png -w 100 -h 12 -- console2svg
```

<!-- c2s:: --format png -w 100 -h 12 -- console2svg -->
![console2svg](/assets/generated/79eee1d64157.png)

## レンダリングエンジン

console2svg は組み込みの [resvg](https://github.com/linebender/resvg)（Rust製の高速SVGレンダラー）を搭載しており、追加ツールのインストールなしで高品質なPNG画像を生成できます。

必要に応じて `--svg-converter` オプションで変換エンジンを切り替えることも可能です。

| 設定値 | 説明 |
| :--- | :--- |
| `auto` | デフォルト。組み込みの resvg を優先して利用します。 |
| `resvg` | 組み込みの resvg を強制します。 |
| `rsvg-convert` | システムの `rsvg-convert` コマンドを使用します。 |
| `ffmpeg` | ffmpeg の librsvg デコーダーを使用します。 |

```bash title="Terminal" "--svg-converter rsvg-convert"
console2svg capture -o result.png --svg-converter rsvg-convert -- console2svg
```

<!-- c2s:: --format png -w 100 -h 12 --svg-converter rsvg-convert -- console2svg -->
![console2svg](/assets/generated/28fd217fb344.png)
