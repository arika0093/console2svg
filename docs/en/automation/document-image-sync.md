---
title: Sync documentation and images
description: Generate example images from Markdown and MDX code blocks and reflect them in documentation.
---

## Motivation

When building this very documentation site, the following points felt troublesome.

* Keeping command examples and output images in sync is troublesome.
  * A straightforward implementation requires preparing a script for image generation.
  * Updating separate files at the same time is difficult, and missing synchronization is easy.
* Until the image is actually generated, you cannot write the image link in the material.
  * For example, when using [Astro](https://docs.astro.build), the build fails if the image does not exist.
  * This is especially problematic for commands that cannot be run locally (for example, commands that only work on another OS).

Therefore, I decided to provide the `batch markdown` subcommand.  
This lets you generate example images from Markdown and MDX code blocks and immediately reflect them in documentation.

> [!TIP]
> You can also incorporate it into a CI/CD pipeline by running it with [GitHub Actions](./github-actions.md).


## Basic usage

The simplest usage is to place a `<!-- c2s:: (command) -->` marker in a markdown file as follows.

```markdown title="example.md"
Running `dotnet --info` produces output like the following.

<!-- c2s:: dotnet --info | head -n10 -->
```

Then run the following command.

```bash title="Terminal" "batch markdown"
console2svg batch markdown -i ./docs -o ./assets
```

The marker in the markdown above is detected, and the command you wrote (in this case, `dotnet --info | head -n10`) is executed.
After that, the captured image is saved in the `./assets` directory.
The markdown file is also updated, and an image is added immediately after the marker.

```diff lang="markdown" title="example.md"
Running `dotnet --info` produces output like the following.

<!-- c2s:: -w 100 -h 12 --- dotnet --info | head -n10 -->
+ ![dotnet --info](../assets/sample-QzOus8.svg)
```

The generated image path is created automatically from information such as the markdown file path and is written as a relative path.
On the second and later runs, the image path immediately after the marker is read and only that image is updated, so image links inside the documentation do not change.

## markdown
### Simple specification

You can specify various options after the `c2s::` marker.

```markdown title="example.md" "-w 100 -h 10 -d macos"
<!-- c2s:: -w 100 -h 10 -d macos -t nord -- dotnet --version -->
```

> [!NOTE]
> Currently, options with side effects other than capture, such as `--save-cast`, `--save-frames`, `--embed-*`, and `--stdout`, cannot be used.

### Setup and teardown

If you write YAML-formatted settings after the marker, you can specify setup before execution, replacement of the command to run, and teardown after execution.

```markdown {4-9}
The following configuration captures only the execution result, without capturing build output.

<!-- c2s:: -w 100 -h 10 -d macos
setup:
  dotnet build
capture: 
  dotnet run --no-build
teardown:
  rm -f temporary-file
-->
```

### Referencing code block contents

If you add `c2s-id` to a named code block, a marker can reference that code block.

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

`{code:<id>}` is replaced with the contents of the specified code block.

### Reusing images

If you want to reuse an image that has already been generated, simply specify the shared destination path and generate it only once.

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
### Specification

Execution results are designed to be basically idempotent.

* If no image exists immediately after the marker, a new image is generated and added.
* If an image exists immediately after the marker, that image is updated.

This makes execution in CI/CD pipelines easier.

### Filters

During development, you can specify glob filters against paths relative to the input directory. Filters can be specified multiple times.

```bash title="Terminal" "--filter"
console2svg batch markdown -i ./docs -o ./docs/assets --filter "reference/**"
```

## Design philosophy

It is designed with the following points in mind.

* Ideally, the written markdown files should remain viewable as-is.
  * Markers are written as HTML comments, so ordinary Markdown renderers ignore them.
  * `c2s-id` for code block references is also ignored by ordinary Markdown renderers.

For example, [this page](../appearance/window-and-background.md) should still be viewable normally even when [viewed on GitHub](https://github.com/arika0093/console2svg/blob/main/docs/appearance/window-and-background.md).

## Notes

> [!WARNING]
> The `setup`, `capture`, `teardown`, and commands written in markers are executed in a shell.
> Do not run this on untrusted Markdown or files checked out from untrusted pull requests.
