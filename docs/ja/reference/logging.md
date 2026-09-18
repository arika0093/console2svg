---
title: ログ出力
description: 詳細ログの表示・保存およびSVGファイルへのデバッグ情報埋め込み機能。
---

問題調査やトラブルシューティング時に利用できるログ機能と、SVGメタデータへの情報埋め込みについて解説します。

## ログ出力オプション

* **`--verbose`**: 詳細な実行ログを標準エラー（stderr）に出力します。
* **`--verbose <path>`** / **`--verbose-log <path>`**: 詳細ログを指定したファイルへ書き出します。

```bash
console2svg capture --verbose-log debug.log -o output.svg -- fastfetch
```

## SVGへのデバッグ情報埋め込み

console2svg では、生成されるSVGドキュメント内部のメタデータ領域に各種診断データを埋め込むことができます。

* **`--embed-logs`**: 実行時のログ出力をSVG内部へ埋め込みます。
* **`--embed-cast`**: 端末イベントストリーム（asciicast v2）を埋め込みます。
* **`--embed-replay`**: キーボード入力イベントを埋め込みます。
* **`--embed-debug`**: 上記（ログ、cast、replay）のすべてを一括で埋め込みます。

```bash
console2svg capture --embed-debug -o debug.svg -- my-app
```

生成されたSVGファイルには見た目上の変化はありませんが、バグ報告時に単一のSVGファイルを共有するだけで、開発側で実行環境のログや入出力内容を詳細に検証できるようになります。
