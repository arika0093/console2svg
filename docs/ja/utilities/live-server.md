---
title: Live-Server
description: 実行中のターミナル画面をSVGストリームとしてブラウザへリアルタイム配信する機能。
---

Live-Server機能を使用すると、ローカルHTTPサーバーを起動し、ターミナル上で実行されている内容をブラウザへリアルタイムにSVG描画できます。

## 起動方法

`live-server` サブコマンドに実行したいコマンドを渡します。

```bash title="Terminal"
console2svg live-server -- btop
```

コマンドを実行すると、標準では`127.0.0.1:38473`でHTTPサーバーが起動します。

```text
Live server listening on http://127.0.0.1:38473/
```

ブラウザでこのURLを開くと、ターミナルの状態が低遅延・ベクター品質で表示されます。

![console2svg live-server](/docs/assets/cmd-liveserver.png)

## オプション

* **`--no-resize`**: ブラウザのウィンドウサイズ変更に伴うターミナルの自動リサイズを無効化します。
* テーマやウインドウスタイル（`-d`, `-t` など）もそのまま適用可能です。

配信ソフト（OBS Studioなど）のブラウザソースとして読み込むことで、高画質なターミナル画面をオーバーレイ表示することもできます。
