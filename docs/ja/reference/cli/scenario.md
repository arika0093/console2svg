---
title: scenario
description: シナリオドキュメントを読み込み、擬似端末上で自動実行するコマンド。
since: v0.11
---

```bash title="Terminal"
console2svg scenario run <document> [options]
```

`scenario` は、YAML または JSON で記述された **シナリオドキュメント**（ScenarioDocument）を読み込み、擬似端末（PTY）上で一連の端末操作を自動実行するサブコマンドです。
手動操作のタイミングに依存していた従来の `replay` とは異なり、画面テキストの出現や消失を待機する **状態同期** に基づいて動作します。
CI 環境での回帰テストや、ドキュメント画像の定期更新において安定した実行結果を得られます。

## コマンド構文と引数

### `scenario run`

指定されたシナリオドキュメントを実行します。

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

* `<document>`：実行対象のシナリオドキュメントのパス（必須）

## オプション

* `--width <width>`：擬似端末の横幅（文字数）を上書きします。
* `--height <height>`：擬似端末の高さ（行数）を上書きします。
* `--no-colorenv`：カラー関連環境変数の自動設定を無効化します。
* `--no-delete-envs`：CI 関連の環境変数を削除せずに維持します。
* `-C, --config <path>`：指定した設定ファイルを読み込み、シナリオの設定へ重ね合わせます。

## シナリオの実行フェーズ

`scenario run` を実行すると、ドキュメント内の各フェーズが以下の順序で処理されます。

1. **workingDir**：指定された作業ディレクトリへ移動します。
   `temporary: true` を指定した場合は一時ディレクトリが作成されます。
2. **prepare**：擬似端末の起動前に、制御シェル上で事前準備コマンドを実行します。
3. **launch**：指定された実行ファイルと引数で擬似端末を起動します。
4. **execute**：定義された操作ステップ（`send`、`wait`、`resize`、`capture`、`command`）を順次実行します。
5. **verify**：操作終了後、制御シェル上で事後検証コマンドを実行します。
6. **teardown**：検証完了後、制御シェル上で後片付けコマンドを実行します。

## execute 内のステップ仕様

### send

端末へ入力データを送信します。
`inputs` 配列に複数の入力を含めると、指定された順序で連続送信します。

```yaml
- type: send
  inputs:
    - text: ":w"
    - keys: Enter
```

* `text`：プレーンテキスト
* `keys`：特殊キー名（`Enter`、`Escape`、`Tab`、`Ctrl+C`、方向キーなど）
* `paste`：ブラケット付き貼り付けに対応した文字列
* `rawHex`：16 進数表記の生バイト列

### wait

画面の表示状態が条件を満たすまで待機します。

```yaml
- type: wait
  args:
    text: "Done"
    until: present
    stableFor: 1s
    timeout: 10s
```

* `text`：完全一致または部分一致させる文字列
* `regex`：正規表現によるパターン一致
* `until`：文字列の出現（`present`）または消失（`absent`）
* `stableFor`：条件が指定時間以上維持されることを要求
* `timeout`：待機の上限時間

### capture

現在の画面を SVG 画像として保存します。

```yaml
- type: capture
  args:
    output: output.svg
  options:
    appearance:
      theme:
        - nord
```

### resize

端末の横幅と高さを変更します。

```yaml
- type: resize
  args:
    width: 120
    height: 30
```

### command

端末セッションの合間に、制御シェル上でコマンドを実行します。

```yaml
- type: command
  command: "ls -la"
```

## 終了コードと実行結果

すべてのステップが正常に完了した場合は終了コード 0 を返します。
いずれかのステップで待機タイムアウトやコマンドの失敗が発生した場合は終了コード 1 を返します。
標準出力には、実行結果を示す JSON が出力されます。

```json title="出力例"
{
  "schemaVersion": 1,
  "status": "completed",
  "sessionId": "s_a1b2c3d4e5f6",
  "exitCode": 0,
  "artifacts": [
    "/path/to/output.svg"
  ]
}
```
