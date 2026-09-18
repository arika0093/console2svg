---
title: 同步文档和图片
description: 从 Markdown 或 MDX 的代码块生成执行示例图片，并反映到文档中。
---

## 动机

正是在构建这个文档站点时，我觉得以下几点很麻烦。

* 保持命令示例和输出图片同步很麻烦。
  * 直接实现时，需要准备生成图片用的脚本。
  * 同时更新不同文件很困难，也容易漏掉同步。
* 在实际生成图片之前，无法把图片链接写进资料中。
  * 例如使用 [Astro](https://docs.astro.build) 时，如果图片不存在，构建会失败。
  * 对于无法在本地执行的命令（例如只能在其他 OS 上运行的命令），这尤其成问题。

因此，我决定提供 `batch markdown` 子命令。  
这样就可以从 Markdown 或 MDX 的代码块生成执行示例图片，并立即反映到文档中。

> [!TIP]
> 通过在 [GitHub Actions](./github-actions.md) 中执行，也可以集成到 CI/CD 流水线中。


## 基本用法

最简单的用法是在 markdown 文件中放置如下 `<!-- c2s:: (command) -->` 标记。

```markdown title="example.md"
运行 `dotnet --info` 会得到如下输出。

<!-- c2s:: dotnet --info | head -n10 -->
```

然后执行以下命令。

```bash title="Terminal" "batch markdown"
console2svg batch markdown -i ./docs -o ./assets
```

这样会检测到上述 markdown 中的标记，并执行其中写入的命令（本例中为 `dotnet --info | head -n10`）。
随后，捕获的图片会保存到 `./assets` 目录。
同时 markdown 文件也会更新，在标记后立即追加图片。

```diff lang="markdown" title="example.md"
运行 `dotnet --info` 会得到如下输出。

<!-- c2s:: -w 100 -h 12 --- dotnet --info | head -n10 -->
+ ![dotnet --info](../assets/sample-QzOus8.svg)
```

生成图片的路径会根据 markdown 文件路径等自动生成，并以相对路径写入。
第二次及之后执行时，会读取标记后紧跟的图片路径，只更新该图片，因此文档中的图片链接不会改变。

## markdown
### 简易指定

可以在 `c2s::` 标记后指定各种选项。

```markdown title="example.md" "-w 100 -h 10 -d macos"
<!-- c2s:: -w 100 -h 10 -d macos -t nord -- dotnet --version -->
```

> [!NOTE]
> 目前不能使用 `--save-cast`、`--save-frames`、`--embed-*`、`--stdout` 等具有捕获以外副作用的选项。

### 设置和后处理

在标记后写入 YAML 格式的设置后，可以指定执行前的设置、执行内容替换以及执行后的后处理。

```markdown {4-9}
如下配置可以不捕获构建时的输出，只捕获执行结果。

<!-- c2s:: -w 100 -h 10 -d macos
setup:
  dotnet build
capture: 
  dotnet run --no-build
teardown:
  rm -f temporary-file
-->
```

### 引用代码块内容

给带名称的代码块添加 `c2s-id` 后，标记就可以引用该代码块。

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

`{code:<id>}` 会被替换为指定代码块的内容。

### 复用图片

如果想复用曾经生成过的图片，只需指定共享目标路径，并只生成一次。

```markdown
## File A

只生成一次的图片。

<!-- c2s:: -- echo shared image! -->
![dotnet --info](../assets/share/share-img.svg)


## File B

在另一个文件中引用它。
![dotnet --info](../assets/share/share-img.svg)
```


## CLI
### 规格

执行结果设计为基本具备幂等性。

* 如果标记后没有图片，则生成并添加新图片。
* 如果标记后已经有图片，则更新该图片。

这样更便于在 CI/CD 流水线中执行。

### 过滤器

开发过程中，可以针对输入目录的相对路径指定 glob 过滤器。过滤器可以多次指定。

```bash title="Terminal" "--filter"
console2svg batch markdown -i ./docs -o ./docs/assets --filter "reference/**"
```

## 设计思想

设计时考虑了以下几点。

* 理想情况下，编写的 markdown 文件应该可以原样浏览。
  * 标记以 HTML 注释形式编写，因此普通 Markdown 渲染器会忽略它们。
  * 用于代码块引用的 `c2s-id` 也会被普通 Markdown 渲染器忽略。

例如，[此页面](../appearance/window-and-background.md)即使在 [GitHub 上查看](https://github.com/arika0093/console2svg/blob/main/docs/appearance/window-and-background.md)，也应该可以正常浏览。

## 注意事项

> [!WARNING]
> 标记中写入的 `setup`、`capture`、`teardown` 以及命令会在 Shell 中执行。
> 请不要对不可信的 Markdown，或从不可信 Pull Request 检出的文件执行此操作。
