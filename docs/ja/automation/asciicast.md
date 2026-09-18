---
title: asciicastファイルの記録/再生
description: asciinema の asciicast v2 形式の保存・再生とSVGファイルへのメタデータ埋め込み。
---

端末録画ツール asciinema で標準的に使用される **asciicast v2** 形式との相互運用が可能です。

## asciicast の記録

`--save-cast` オプションを指定することで、実行出力を asciicast 形式（`.cast`）として保存できます。

```bash title="Terminal" "--save-cast session.cast"
console2svg capture --save-cast session.cast -- cargo build
```

保存された `.cast` ファイルは、通常の `asciinema play` 等でも再生できます。

### SVGへのデータ埋め込み (`--embed-cast`)

`--embed-cast` オプションを指定すると、生成されるSVGファイル内部のメタデータ領域に asciicast データを埋め込むことができます。

```bash title="Terminal" "--embed-cast"
console2svg capture --embed-cast -o output.svg -- fastfetch
```

## asciicast の再生と変換

既存の `.cast` ファイルからSVGや動画をレンダリングするには、`cast` サブコマンドを使用します。

```bash title="Terminal" "cast session.cast"
# 静止画SVGとして出力
console2svg cast session.cast -o session.svg

# 動画（GIF）として出力
console2svg cast session.cast -v -o session.gif -t monokai
```

または `capture --in` を指定します。

```bash title="Terminal" "--in session.cast"
console2svg capture --in session.cast -d macos-pc -o output.svg
```
