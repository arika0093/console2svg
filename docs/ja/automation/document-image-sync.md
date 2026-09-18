---
title: ドキュメントと画像を同期する
description: MarkdownやMDXのコードブロックから実行例の画像を生成し、ドキュメントへ反映する
---

## 動機
まさにこのドキュメントサイトを構築している際に、以下の点が面倒だと感じました。

* コマンド例と出力画像の同期を取るのが面倒。
  * 素直に実装すると、画像生成用のスクリプトを用意する必要がある。
  * 別々のファイルを同時に更新するのは難しく、同期漏れが発生しやすい。
* 実際に画像を生成するまで、画像のリンクを資料内に書くことができない。
  * 例えば[Astro](https://docs.astro.build)を使用する場合、画像がないとビルドに失敗する。
  * これはローカルで実行できないコマンド(例えば別OSでしか動かないもの)で特に問題になる。

そこで、`batch markdown` サブコマンドを用意することにしました。  
これにより、MarkdownやMDXのコードブロックから実行例の画像を生成し、ドキュメントへ即時反映することができます。

> [!TIP]
> [GitHub Actions](./github-actions.md)で実行することで、CI/CDパイプラインに組み込むこともできます。


## 基本的な使い方
最も簡単な使い方は、以下のように `<!-- c2s:: (command) -->` マーカーをmarkdownファイル内に配置するだけです。

```markdown title="example.md"
`dotnet --info`を実行すると以下のような出力が得られます。

<!-- c2s:: dotnet --info | head -n10 -->
```

その後、以下のコマンドを実行します。

```bash title="Terminal" "batch markdown"
console2svg batch markdown -i ./docs -o ./assets
```

すると、上記markdown内のマーカーが検出され、記載したコマンド(上記の場合は `dotnet --info | head -n10`) が実行されます。
その後、キャプチャされた画像が `./assets` ディレクトリに保存されます。
また、markdownファイルも更新され、マーカーの直後に画像が追加されます。

```diff lang="markdown" title="example.md"
`dotnet --info`を実行すると以下のような出力が得られます。

<!-- c2s:: -w 100 -h 12 --- dotnet --info | head -n10 -->
+ ![dotnet --info](../assets/sample-QzOus8.svg)
```

生成された画像のパスは、markdownファイルのパス等から自動で生成され、相対パスで記述されます。
2回目以降の実行では、マーカーの直後にある画像パスを読み取り、その画像を更新するだけなので、ドキュメント内の画像リンクは変更されません。

## markdown
### 簡易的な指定

`c2s::` マーカーの後ろに各種オプションを指定できます。

```markdown title="example.md" "-w 100 -h 10 -d macos"
<!-- c2s:: -w 100 -h 10 -d macos -t nord -- dotnet --version -->
```

> [!NOTE]
> 現在, `--save-cast`、`--save-frames`、`--embed-*`、`--stdout` など、キャプチャ以外の副作用を持つオプションは使用できません。

### セットアップと後処理

マーカーの後ろにYAML形式の設定を記述すると、実行前のセットアップ、実行内容の置き換え、実行後の後処理を指定できます。

```markdown {4-9}
以下のように記述することで、ビルド時の出力をキャプチャせず、実行結果のみをキャプチャすることができます。

<!-- c2s:: -w 100 -h 10 -d macos
setup:
  dotnet build
capture: 
  dotnet run --no-build
teardown:
  rm -f temporary-file
-->
```

### コードブロックの中身を参照する

名前付きのコードブロックに `c2s-id` を付けると、マーカーからそのコードブロックを参照できます。

````markdown title="example.md" "{code:program}" "{code:result}" "c2s-id=program" "c2s-id=result"
```csharp c2s-id=program
Console.WriteLine("hello");
```

```bash c2s-id=result
cat result.txt
```

<!-- c2s:: -o examples/hello.svg
setup: |
  cat > test.cs <<'EOF'
  {code:program}
  EOF
  dotnet run test.cs > result.txt
capture: "{code:result}"
teardown: rm -f test.cs result.txt
-->
````

`{code:<id>}` は、指定したコードブロックの内容に置き換えられます。

### 画像を使い回す

一度生成した画像を使いまわしたい場合、単純に共有先のパスを指定し、一度だけ生成するだけです。

```markdown
## File A

Generated image once.

<!-- c2s:: -- echo shared image! -->
![dotnet --info](../assets/share/share-img.svg)


## File B

And reference it in another file.
![dotnet --info](../assets/share/share-img.svg)
```


## CLI
### 仕様

実行結果は基本的に冪等になるように設計されています。

* マーカーの直後に画像が存在しない場合、新しい画像を生成して追加します。
* マーカーの直後に画像が存在する場合、その画像を更新します。

これにより、CI/CDパイプラインでの実行が容易になります。

### フィルター

開発中は、入力ディレクトリからの相対パスに対してグロブフィルターを指定できます。フィルターは複数回指定できます。

```bash title="Terminal" "--filter"
console2svg batch markdown -i ./docs -o ./docs/assets --filter "reference/**"
```

## 設計思想
以下の点を意識して設計されています。

* 記述されたmarkdownファイルは、理想的にはそのまま閲覧できるべきです。
  * マーカーはHTMLコメントとして記述されるため、通常のMarkdownレンダラーでは無視されます。
  * コードブロック参照のための `c2s-id` も、通常のMarkdownレンダラーでは無視されます。

たとえば、[このページ](../appearance/window-and-background.md)は、[GitHub上で閲覧](https://github.com/arika0093/console2svg/blob/main/docs/appearance/window-and-background.md)しても通常通り閲覧できるはずです。

## 注意事項

> [!WARNING]
> マーカーに記述された `setup`、`capture`、`teardown` およびコマンドはシェルで実行されます。
> 信頼できないMarkdownや、信頼できないPull Requestからチェックアウトしたファイルに対して実行しないでください。
