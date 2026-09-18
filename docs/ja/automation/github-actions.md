---
title: GitHub Actionsで使う
description: CIパイプライン上でCLIスクリーンショットを自動生成・コミットするためのワークフロー例。
---

GitHub Actionsと組み合わせることで、リポジトリの更新やリリースに合わせてREADMEの画像を自動更新できます。

## GitHub Actions

CIで使用するための便利なGitHub Actionも利用できます。最新バージョンの `console2svg` を使用するには、ワークフローに次のステップを追加するだけです：

```yaml
- uses: arika0093/console2svg@main
```

console2svgのバージョンを指定するには、以下のように指定します。

```yaml
- uses: arika0093/console2svg@main
  with:
    version: 0.8.3
    # ソースからビルドしたい場合
    # version: develop
```

> [!NOTE]
> このドキュメントサイトの画像は、上記Actionsを使って自動生成されています。


## ワークフロー例
### 単発の生成
以下は、Ubuntuランナー上で console2svg をインストールしてSVGを生成し、Gitリポジトリへコミットするワークフローの例です。

```yaml
jobs:
  gen:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVG
        run: console2svg capture -w 120 -c -d macos-pc -o output.svg -- dotnet --version

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVG"
```

### 自動同期
[batch markdown](./document-image-sync.md) 機能を使用することで、ドキュメントの自動更新を行うこともできます。
以下は、Markdown内のマーカーを検出して画像を生成し、コミットするワークフローの例です。

```yaml
jobs:
  sync:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Setup console2svg
        uses: arika0093/console2svg@main

      - name: Generate SVGs from Markdown
        run: console2svg batch markdown -i ./docs -o ./assets

      - name: Commit Changes
        uses: stefanzweifel/git-auto-commit-action@v7
        with:
          commit_message: "[skip-ci] chore: regenerate SVGs"
```

> [!WARNING]
> markdown内に埋め込まれた悪意あるコードが実行される可能性があるため、実行タイミングには細心の注意を払ってください。
> 特に、外部からのPRやブランチのマージ時に自動実行する場合は、信頼できるコードのみが実行されるようにしてください。

## 環境変数の自動上書き

一部のライブラリ（例：[chalk](https://www.npmjs.com/package/chalk)）はCI環境を検出し、自動的に色出力を無効にします。
しかし、SVGを生成する場合は常に色を有効にしたいはずです。したがって、console2svgはデフォルトで以下の環境変数を自動的に設定・削除します：
* `TERM=xterm-256color`：色サポートを有効にするために設定されます。
* `COLORTERM=truecolor`：TrueColor出力を有効にします。
* `FORCE_COLOR=3`：同上。`3`はTrueColorを示します。
* `CI`（削除）：CI環境を検出するとライブラリが色を無効にするため、意図的に削除されます。
* `TF_BUILD`（削除）：同じ理由で削除されます（Azure Pipelinesで使用）。

この動作を無効にするには、`--no-colorenv`と`--no-delete-envs`オプションを使用します。