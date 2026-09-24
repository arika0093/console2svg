---
title: LLMにターミナルを操作させる
description: console2svgをLLMに操作させてキャプチャや実行結果の確認を行う
since: v0.11
---

LLMが操作する用途に構造化されたターミナル操作を利用することができます。

## 動機
ターミナル向けの[`playwright-cli`](https://github.com/microsoft/playwright-cli)のようなツールを提供し、AIの"目"の役割を提供します。

これにより、見た目の改善のような視覚的要望に対して、LLMが自己完結的にターミナル操作を行い、実行結果の確認ループを行うことができます。

## 使用方法

[SKILL.md](./use-skill.md)をLLMに読み込ませます。

## 概要

[console2svg session](../reference/cli/session.md)を使用して、LLMがターミナルを操作することができます。

### セッションの起動

起動するとセッションIDが返されます。

```bash title="Terminal"
# セッション開始
console2svg session start -- bash
# > {"sessionId":"s_randomhash1234","state":"running", ...}
```

### サイズ変更
一部のTUIは、起動時のサイズに依存する場合があります。必要に応じてサイズを変更します。

```bash title="Terminal"
console2svg session resize s_randomhash1234 --width 160 --height 40
```

### 画面の確認

現状を確認するために、画面のテキストや画像を取得します。

```bash title="Terminal"
# ターミナル上のtextを取得する
console2svg session read s_randomhash1234 --wait 1s
# 画像を取得する
console2svg session capture s_randomhash1234 -o /tmp/current-screen.svg
```

### 入力の送信

TUIを操作するための入力を送信します。

```bash title="Terminal"
# 文字列を送信する
console2svg session send s_randomhash1234 --text "search query"
# 特殊キーを送信する
console2svg session send s_randomhash1234 --keys Enter
```

### セッションの停止

```bash title="Terminal"
# セッションを停止する
console2svg session stop s_randomhash1234
# すべてのセッションを停止する
console2svg session stop --all --yes
```
