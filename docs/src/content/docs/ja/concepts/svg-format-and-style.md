---
title: SVGの形式とスタイル
description: console2svgが生成するSVGの構造と、アニメーション・メタデータの扱いを説明します。
---

console2svgが生成するSVGは解像度に依存せず、Markdownへ埋め込んだり、そのままブラウザで開いたりできます。

```text
<svg>
  <metadata> <!-- 任意: replay / cast / verbose log -->
  <defs>     <!-- gradient、filterなど -->
  <g ...>    <!-- 背景・ウインドウ装飾 -->
  <g ...>    <!-- 端末の文字・色 -->
  <style>/<animate> <!-- アニメーションSVGの場合 -->
</svg>
```

## 文字とレイアウト

端末の文字はSVGの`<text>` / `<tspan>`として描画されます。`-w`/`-h`はPTY上の端末サイズ、`--padding`、`--margin`、`--pc-padding`は周囲のレイアウトを調整します。

カスタムWebフォントそのものをSVGへ埋め込むわけではないため、別環境でも同じ見た目を重視する場合は、利用環境に存在するmonospace系フォントを`--font`へ指定してください。

## アニメーション

SVG出力では、標準では最終フレームだけを描画します。`-v` (`--video`)を指定するとフレーム列をアニメーションSVGとして埋め込み、`--fps`、`--sleep`、`--timing`などでタイミングを調整できます。

## 埋め込みメタデータ

`--embed-replay`、`--embed-cast`、`--embed-logs`は任意機能です。指定した情報はSVGの`<metadata>`へ埋め込まれます。

> [!CAUTION]
> 埋め込みデータはSVGファイルと一緒に配布されます。公開ドキュメントでは基本的に埋め込まず、必要な場合も内容を確認してから公開してください。

見た目を揃える場合は、個別に`--forecolor`や`--backcolor`を積み重ねるより、[テーマ](/ja/appearance/themes/)を使う方が管理しやすくなります。
