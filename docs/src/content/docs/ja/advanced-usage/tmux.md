---
title: tmux
description: tmuxのpaneをSVGとしてキャプチャ、またはブラウザへライブ表示します。
---

console2svgはLinuxとmacOSでtmuxのpaneを直接扱えます。WindowsではWSL内で実行してください。すでにtmux上で作業しているセッションを、そのまま画像にしたい場合に便利です。

## paneをキャプチャする

```bash
console2svg tmux capture \
  --target :0 \
  --history \
  -h 12 \
  -o capture.svg
```

`--target`を省略すると、対象paneを対話的に選択できます。

`--history`を付けるとscrollbackも含めます。行数を限定したい場合は値を指定してください。

```bash
console2svg tmux capture --target :0 --history 1000 -o capture.svg
```

## paneをLive Serverで表示する

```bash
console2svg tmux live-server 127.0.0.1:8080 --target :0
```

`tmux live-server`はブラウザへ現在のpaneを配信します。`--history`は`tmux capture`専用で、Live Serverでは利用できません。また、Live Serverはファイルを書き出さないため`-o`も使いません。

![tmux paneのキャプチャ](/assets/cmd-tmux-cap.svg)

![tmuxセッションをリプレイして生成したアニメーションSVG](/assets/cmd-tmux-replay.svg)
