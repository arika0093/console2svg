---
title: scenario
description: Command to execute Scenario documents automatically in a managed pseudo-terminal.
since: v0.11
---

```bash title="Terminal"
console2svg scenario run <document> [options]
```

`scenario` is a subcommand that loads a **Scenario document** (ScenarioDocument) written in YAML or JSON, and executes a sequence of terminal operations deterministically in a pseudo-terminal (PTY).
Unlike the legacy `replay` command which relied on keystroke timing, `scenario` operates via **state synchronization** (waiting for screen text or state changes before continuing).
This ensures reliable execution in CI environments and automated documentation workflows.

## Subcommands and Arguments

### `scenario run`

Executes the specified Scenario document.

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

* `<document>`: Path to the Scenario document (required)

## Options

* `--width <width>`: Overrides pseudo-terminal width (columns).
* `--height <height>`: Overrides pseudo-terminal height (rows).
* `--no-colorenv`: Disables automatic color environment variable injection.
* `--no-delete-envs`: Preserves CI-related environment variables.
* `-C, --config <path>`: Loads an additional configuration file and overlays it onto scenario options.

## Execution Lifecycle

When `scenario run` is invoked, the document phases are processed in the following order:

1. **workingDir**: Switches to the specified directory (`temporary: true` creates a temp directory).
2. **prepare**: Runs setup commands in the control shell before spawning the PTY.
3. **launch**: Spawns the specified executable and arguments in the application PTY.
4. **execute**: Executes terminal steps (`send`, `wait`, `resize`, `capture`, `command`) in order.
5. **verify**: Runs assertion commands in the control shell after terminal execution completes.
6. **teardown**: Runs cleanup commands in the control shell after verification.

## Execute Step Types

### send

Sends input data to the terminal.
Multiple inputs under `inputs` are sent in argument order.

```yaml
- type: send
  inputs:
    - text: ":w"
    - keys: Enter
```

* `text`: Plain text
* `keys`: Named key (`Enter`, `Escape`, `Tab`, `Ctrl+C`, arrow keys, etc.)
* `paste`: Text transmitted using bracketed-paste mode
* `rawHex`: Hexadecimal byte sequence

### wait

Waits for terminal screen state to satisfy a condition.

```yaml
- type: wait
  args:
    text: "Done"
    until: present
    stableFor: 1s
    timeout: 10s
```

* `text`: Literal text to match
* `regex`: Regular expression pattern to match
* `until`: Wait for text to appear (`present`) or disappear (`absent`)
* `stableFor`: Duration the condition must remain true
* `timeout`: Maximum wait duration

### capture

Captures the current terminal screen as an SVG image.

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

Resizes terminal width and height.

```yaml
- type: resize
  args:
    width: 120
    height: 30
```

### command

Runs a shell command in the control shell between terminal steps.

```yaml
- type: command
  command: "ls -la"
```

## Exit Codes and Output

Returns exit code 0 when all steps succeed.
Returns exit code 1 if any step times out or a command fails.
The execution summary is output to standard output in JSON format:

```json title="Sample Output"
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
