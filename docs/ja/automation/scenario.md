---
title: シナリオによる操作の自動化
description: 状態同期とセマンティックな手順定義に基づき、対話的端末操作を確実に再現するシナリオ機能。
since: v0.11
---

`console2svg` では、端末に対する一連の操作手順を **シナリオ**（ScenarioDocument）として定義し、同一の操作を再現できます。
CI パイプラインでの回帰テストや、ドキュメントに掲載する画像の定期更新に利用できます。

従来の `replay` 機能は、手動操作時の経過時間とキーストロークをそのまま再生する方式でした。
この方式では、マシンの負荷やネットワーク速度の差異によって描画が遅延した際、意図しないタイミングでキーが送信されて失敗する問題がありました。
「シナリオ」は、画面上の特定文字列や状態遷移を待機する **状態同期** を中心に設計されており、環境の差異に左右されない安定した実行を実現します。

## シナリオファイルの基本構造

シナリオファイルは YAML 形式（または JSON 形式）で記述します。

```yaml title="demo-scenario.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ScenarioDocument.v1.json
$version: 1

options:
  terminal:
    width: 100
    height: 24

scenario:
  launch:
    executable: vim
    args:
      - demo.txt
  execute:
    - type: wait
      args:
        text: "~"
    - type: send
      inputs:
        - text: "iHello, console2svg!"
        - keys: Enter
        - keys: Escape
    - type: capture
      args:
        output: output.svg
      options:
        appearance:
          theme:
            - nord
```

## シナリオの実行ライフサイクル

シナリオは以下の順序でライフサイクルを進行します。

1. **workingDir**：作業ディレクトリを設定します（一時ディレクトリの自動生成も可能）。
2. **prepare**：擬似端末の起動前に、制御シェル上で事前準備コマンド（ファイル生成など）を実行します。
3. **launch**：指定された実行可能ファイル（`executable`）と引数（`args`）を擬似端末内で起動します。
4. **execute**：端末に対するキー送信、条件待機、画面キャプチャを順番に実行します。
5. **verify**：操作終了後、制御シェル上で事後検証コマンド（ファイルの存在確認など）を実行します。
6. **teardown**：検証完了後、制御シェル上で後片付けコマンドを実行します。

## execute 内の主要ステップ

`execute` 配下には、実行する操作を順序どおりに記述します。

### send

端末へ文字列や特殊キーを送信します。
`inputs` に複数の入力を並べると、指定された順序で連続送信します。

```yaml
- type: send
  inputs:
    - text: "npm test"
    - keys: Enter
```

各入力項目には、以下のいずれか一つを指定します。

* `text`：通常の文字列
* `keys`：特殊キー（`Enter`、`Escape`、`Tab`、`Ctrl+C`、矢印キーなど）
* `paste`：ブラケット付き貼り付けモードに対応した貼り付け文字列
* `rawHex`：16 進数表記の生バイト列（例: `1B5B41`）

### wait

画面の表示状態が指定条件を満たすまで待機します。

```yaml
- type: wait
  args:
    text: "Compiled successfully"
    until: present
    stableFor: 1s
    timeout: 30s
```

* `text`：検出対象の文字列
* `regex`：正規表現による検出パターン
* `until`：文字列の出現（`present`）または消失（`absent`）
* `stableFor`：条件が指定時間以上維持されるまで待機
* `timeout`：待機の上限時間

### capture

現在の端末画面を SVG 画像として保存します。
ステップ個別の外観テーマや装飾オプションを指定できます。

```yaml
- type: capture
  args:
    output: docs/assets/tui-screen.svg
  options:
    appearance:
      theme:
        - dracula
```

### resize

端末の幅と高さを動的に変更します。

```yaml
- type: resize
  args:
    width: 120
    height: 36
```

### command

端末セッションの途中で、ホスト環境の制御コマンドを実行します。

```yaml
- type: command
  command: "touch /tmp/flag-ready"
```

## シナリオの実行方法

作成したシナリオファイルは、`console2svg scenario run` コマンドで実行します。

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

コマンドラインから端末サイズを一時的に変更して実行することもできます。

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml --width 120 --height 30
```

## 対話セッションからのエクスポート

シナリオは手作業で記述するだけでなく、LLM や開発者が `console2svg session` で対話的に進めた操作履歴から書き出すこともできます。

```bash title="Terminal"
console2svg session export s_abc123 -o generated-scenario.yaml
```

エクスポート処理は、セッション内で行われた `send` や `wait`、`capture` を抽出してシナリオを生成します。
一度成功した操作パスをシナリオとして保存すれば、次回以降は人間や LLM が介在することなく自動テストとして繰り返し再利用できます。

## シナリオファイルの完全な例

以下は、シナリオドキュメントの完全な記述例です。ライフサイクル（事前準備、起動、操作実行、事後検証、後処理）の全体像と、各ステップの指定方法を示します。

```yaml title="full-scenario.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ScenarioDocument.v1.json
$version: 1

# 1. シナリオ実行時の共通設定（各 capture ステップの基底設定）
# console2svg.config.yaml の内容を記述可能。
options:
  terminal:
    width: 160
    height: 32
  appearance:
    theme:
      - nord
    window: macos-pc

# 2. 実行するシナリオ本体の定義
scenario:
  # 作業ディレクトリの設定（省略時はコマンド実行時のカレントディレクトリ）
  working-dir:
    path: "/tmp/test-scenario"
    # または一時ディレクトリを自動生成してその中で実行する場合(標準ではこちら)
    # temporary: true

  # 事前準備フェーズ: PTY 起動前に制御シェル上で実行するコマンド群
  prepare:
    - type: command
      command: |
        echo "Preparing test environment..."

  # アプリケーション起動フェーズ: 擬似端末内で起動するコマンド
  launch:
    executable: vim
    args:
      - hello.txt
    options:
      # launch 時のみ適用される端末セル寸法の上書き
      terminal:
        width: 160
        height: 32

  # 操作実行フェーズ: launch したアプリケーションに対して順次実行する操作群
  execute:
    # 例: 挿入モードに入り、テキストを入力して改行
    - type: send
      inputs:
        - text: "i"
        - text: "hello world"
        - keys: Enter

    # 例: "hello world" という文字列が画面上に表示され、2 秒間安定するまで待機
    - type: wait
      args:
        text: "hello world"
        until: present
        stable-for: 2s
        timeout: 5s

    # 例: Esc キーを押し、別名でファイル保存コマンドを実行
    - type: send
      inputs:
        - keys: Esc
        - text: ":w hello.log"
        - keys: Enter

    # 例: 端末操作の合間に制御コマンドを実行し、ファイル生成を確認
    - type: command
      command: |
        test -f hello.log

    # 例: 現在の画面状態を SVG 画像としてキャプチャ
    - type: capture
      args:
        output: hello-world.svg
      options:
        # この capture ステップ限定で外観や動画設定を上書き可能
        appearance:
          theme:
            - dracula

    # 例: vim を終了
    - type: send
      inputs:
        - text: ":q"
        - keys: Enter

  # 事後検証フェーズ: vim 終了後、制御シェル上で終了コード 0 を検証
  verify:
    - type: command
      command: |
        grep "hello world" hello.log

  # 後処理フェーズ: 検証完了後、制御シェル上で一時ファイル等の後片付けを実行
  teardown:
    - type: command
      command: |
        rm hello.log
```
