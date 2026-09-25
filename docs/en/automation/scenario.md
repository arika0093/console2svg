---
title: Automated Operations with Scenarios
description: Reproduce terminal interactions reliably using state synchronization and semantic action definitions.
since: v0.11
---

`console2svg` allows defining a sequence of terminal operations as a **scenario** (ScenarioDocument) to reproduce identical interactions.
Scenarios are designed for regression testing in CI pipelines and automated regeneration of documentation images.

The legacy `replay` command replayed manual keystrokes against recorded timestamps.
That approach was susceptible to timing drift caused by CPU load or rendering delays.
In contrast, scenarios rely on **state synchronization**—waiting for specific screen text or stable states before dispatching input—ensuring deterministic reproduction across different environments.

## Basic Structure

Scenario files are written in YAML (or JSON).

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

## Scenario Lifecycle

A scenario progresses through the following lifecycle phases:

1. **workingDir**: Sets the working directory (supports automatic temporary directory creation).
2. **prepare**: Runs setup commands in a control shell before starting the PTY.
3. **launch**: Spawns the specified `executable` and `args` inside the PTY.
4. **execute**: Executes terminal inputs, condition waits, and captures in sequence.
5. **verify**: Runs assertion commands in the control shell after terminal execution completes.
6. **teardown**: Runs cleanup commands in the control shell after verification.

## Execution Steps

The `execute` list contains ordered actions to run against the application PTY.

### send

Sends text or semantic keys to the terminal.
Multiple inputs specified under `inputs` are transmitted in order.

```yaml
- type: send
  inputs:
    - text: "npm test"
    - keys: Enter
```

Each input item specifies exactly one of:

* `text`: Plain text string
* `keys`: Named key or key combination (`Enter`, `Escape`, `Tab`, `Ctrl+C`, arrow keys, etc.)
* `paste`: Bracketed-paste string
* `rawHex`: Raw byte sequence in hexadecimal (e.g. `1B5B41`)

### wait

Waits for terminal screen state to satisfy a condition.

```yaml
- type: wait
  args:
    text: "Compiled successfully"
    until: present
    stableFor: 1s
    timeout: 30s
```

* `text`: Literal string to match
* `regex`: Regular expression pattern to match
* `until`: Wait for string appearance (`present`) or disappearance (`absent`)
* `stableFor`: Require the condition to remain satisfied for this duration
* `timeout`: Maximum wait duration

### capture

Captures the current terminal screen as an SVG image.
Step-specific themes and window styles can be configured.

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

Dynamically changes the terminal dimensions.

```yaml
- type: resize
  args:
    width: 120
    height: 36
```

### command

Runs a host shell command during the execution phase.

```yaml
- type: command
  command: "touch /tmp/flag-ready"
```

## Running Scenarios

Run a scenario document using `console2svg scenario run`:

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml
```

Override terminal dimensions from the command line:

```bash title="Terminal"
console2svg scenario run demo-scenario.yaml --width 120 --height 30
```

## Exporting from Managed Sessions

Scenarios can be authored by hand or exported directly from interactive sessions managed via `console2svg session`:

```bash title="Terminal"
console2svg session export s_abc123 -o generated-scenario.yaml
```

Export extracts the successful sequence of `send`, `wait`, and `capture` operations into a clean Scenario document.
Once exported, the scenario can be re-run in CI without needing an LLM or human in the loop.

## Complete Scenario Example

Below is a complete Scenario document example based on `todo/171-scenario-config-file.md`, illustrating the full lifecycle (prepare, launch, execute, verify, teardown):

```yaml title="full-scenario.yaml"
# yaml-language-server: $schema=https://raw.githubusercontent.com/arika0093/console2svg/main/schema/ConsoleToSvg.ScenarioDocument.v1.json
$version: 1

# 1. Base options for scenario execution (base settings for each capture step)
# You can include the contents of console2svg.config.yaml here.
options:
  terminal:
    width: 160
    height: 32
  appearance:
    theme:
      - nord
    window: macos-pc

# 2. Scenario definition
scenario:
  # Working directory (defaults to the current directory when omitted)
  working-dir:
    path: "/tmp/test-scenario"
    # Or create a temporary directory automatically (the default)
    # temporary: true

  # Prepare: Runs setup commands in the control shell before launching PTY
  prepare:
    - type: command
      command: |
        echo "Preparing test environment..."

  # Launch: Executable and arguments spawned inside the application PTY
  launch:
    executable: vim
    args:
      - hello.txt
    options:
      # PTY dimension override applied only at launch
      terminal:
        width: 160
        height: 32

  # Execute: Ordered terminal actions run against the launched process
  execute:
    - type: send
      inputs:
        - text: "i"
        - text: "hello world"
        - keys: Enter

    # Wait until "hello world" is visible and stable for 2 seconds
    - type: wait
      args:
        text: "hello world"
        until: present
        stable-for: 2s
        timeout: 5s

    # Save to a log file
    - type: send
      inputs:
        - keys: Esc
        - text: ":w hello.log"
        - keys: Enter

    # Shell assertion during execution
    - type: command
      command: |
        test -f hello.log

    # Capture current screen to SVG with step-specific theme
    - type: capture
      args:
        output: hello-world.svg
      options:
        appearance:
          theme:
            - dracula

    # Quit vim
    - type: send
      inputs:
        - text: ":q"
        - keys: Enter

  # Verify: Run assertion commands in control shell after application exits
  verify:
    - type: command
      command: |
        grep "hello world" hello.log

  # Teardown: Cleanup commands in control shell
  teardown:
    - type: command
      command: |
        rm hello.log
```
