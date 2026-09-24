---
title: tmux
description: tmuxペインを記録または配信するコマンド。
---

```bash title="Terminal"
console2svg tmux capture --target <pane> [options]
console2svg tmux live-server --target <pane> [options] [host:port]
```

`capture`は指定ペインをSVGに記録し、`live-server`はそのペインをライブ配信します。`--history`で遡って取得する履歴行数を指定できます。

`capture`に`--json`を追加すると、ペインのプレーンテキスト、寸法、生成アーティファクトのパスを取得できます。

```bash title="Terminal"
console2svg tmux capture --target %1 --json
```

JSONの各フィールドは[`capture`リファレンス](./capture.md)を参照してください。

## `capture`のオプション

`capture`では[`capture`](./capture.md)のオプションを利用できます。

### `--target <pane>`

記録するtmuxペインを指定します（例: `:0.1`）。

### `--history [lines]`

ペインの履歴をすべて、または指定した行数だけ含めます。値を省略すると履歴全体を取得します。

## `live-server`のオプション

`live-server`では[`live-server`](./live-server.md)のオプションを利用できます。

### `--target <pane>`

配信するtmuxペインを指定します。
