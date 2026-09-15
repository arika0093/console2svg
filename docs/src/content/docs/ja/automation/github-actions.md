---
title: GitHub Actions
description: GitHub Actions上でconsole2svgをセットアップして画像を生成します。
---

リポジトリにはconsole2svgをセットアップするComposite Actionが含まれています。`uses: arika0093/console2svg@main`はAction定義を`main`から読み込みますが、インストールするconsole2svg本体は`version`入力で決まります。`version`を省略した場合は`latest`です。

```yaml
- uses: actions/checkout@v4

- name: Setup console2svg
  uses: arika0093/console2svg@main
  with:
    version: latest # 再現性を重視する場合は固定のリリース番号に置き換えてください

- name: Capture command output
  run: |
    console2svg capture -w 120 -h 30 -c -d macos-pc \
      -o output.svg -- dotnet --info

- name: Upload capture
  uses: actions/upload-artifact@v4
  with:
    name: console-capture
    path: output.svg
```

CIで毎回同じバイナリを使いたい場合は、`version`に具体的なリリース番号を指定してください。

開発中のソースをその場でビルドして試したい場合は`develop`を指定します。この場合、Action内で.NET SDKとRustを用意してソースからビルドします。

```yaml
- name: Setup console2svg from source
  uses: arika0093/console2svg@main
  with:
    version: develop
```

成果物の再現性や機密情報の扱いについては[CI/CDで使う](/ja/automation/ci-cd-support/)を参照してください。

## 生成した画像を確認する

workflow runの**Artifacts**から`console-capture`をダウンロードし、`output.svg`をブラウザで開けば確認できます。上の例ではverboseログやリプレイファイルはアップロードしていません。デバッグ目的で保存する場合も、内容を確認するまでは公開しないでください。
