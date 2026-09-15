---
title: テーマ
description: 組み込みテーマやカスタムテーマでターミナルの見た目を変更します。
---

テーマはconsole2svgの見た目をまとめて変更するための仕組みです。端末のカラーパレット、ウインドウ装飾、デスクトップ背景、レイアウト初期値などを持てます。

`-d` (`--window`)はウインドウ装飾だけを指定するための短縮手段です。再利用する見た目を定義したい場合は`--theme` (`-t`)を使うと分かりやすいでしょう。

## テーマを指定する

```bash
console2svg capture --theme tokyo-night -- git status
```

`--theme`は複数回指定できます。設定が重なった場合は、後に指定したテーマの値が優先されます。

```bash
console2svg capture --theme tokyo-night --theme macos-pc -c -- fastfetch
```

## 組み込みの端末カラーパレット

| Theme ID | 説明 |
| --- | --- |
| `dark` | 標準のダーク配色 |
| `light` | ライト配色 |
| `dracula` | Dracula風のダーク配色 |
| `github-dark`, `github-light` | GitHub風の配色 |
| `gruvbox-dark`, `gruvbox-light` | Gruvbox配色 |
| `matrix` | 黒地に緑のMatrix風配色 |
| `nord` | Nord配色 |
| `one-light` | One Light配色 |
| `solarized-dark`, `solarized-light` | Solarized配色 |
| `tokyo-night` | Tokyo Night配色 |

## 組み込みのウインドウ装飾

| Theme ID | 説明 |
| --- | --- |
| `none` | ウインドウ装飾なし |
| `transparent` | 背景を透明にしたテキスト中心の表示 |
| `macos`, `windows` | macOS / Windows Terminal風のコンパクトな装飾 |
| `macos-pc`, `windows-pc` | 外側の余白と影を含むデスクトップ風の装飾 |

`-d macos-pc`は`macos-pc`のウインドウ装飾を選ぶ指定です。

## フルテーマとカスタムテーマ

`cyberpunk`や`cyberpunk-pc`のように、端末配色と周辺装飾をまとめたテーマもあります。利用できるテーマは、インストール済みのバージョンで確認するのが確実です。

```bash
console2svg theme list
console2svg theme list --format markdown
```

カスタムテーマはディレクトリ、アーカイブ、URLからインストールできます。

```bash
console2svg theme install <package-or-source>
console2svg theme update
console2svg theme remove <theme-id>
```

テーマのファイル形式については[ファイル形式と埋め込みメタデータ](/ja/reference/file-formats-and-embedded-metadata/)を参照してください。
