---
title: 幅と高さを指定する
description: ターミナルの表示領域を固定する方法。
---

`capture` の出力サイズは、デフォルトではコマンドや端末が使用する幅・高さに合わせて決まります。
`-w` / `--width` で横幅、`-h` / `--height` で高さを文字セル単位で指定できます。

## 幅と高さを固定する

```bash title="Terminal" "-w 50 -h 5"
console2svg capture -w 50 -h 5 -- console2svg
```

<!-- c2s::  -w 50 -h 5 -- console2svg -->
![console2svg](/assets/generated/57425e021433.svg)

この例では、横50文字・縦5行のターミナル領域を作成します。
出力するコマンドが領域を超える場合は、端末と同じように折り返しやスクロールが発生します。

## 幅または高さだけ指定する

幅と高さは個別に指定できます。指定しなかった方は、通常の端末サイズやコマンドの出力に合わせて決まります。

```bash title="Terminal" "-w 50" "-h 5"
# 横幅だけを50文字に固定
console2svg capture -w 50 -- console2svg

# 高さだけを5行に固定
console2svg capture -h 5 -- console2svg
```
