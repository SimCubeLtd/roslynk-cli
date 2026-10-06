# Running and troubleshooting

Read this when installation, source execution, workspace loading or daemon freshness is relevant. For command parameters and supported scopes, use [tools.md](tools.md).

## Installed CLI

Requires the .NET 10 SDK on PATH. Install with `dotnet tool install Roslynk-cli --global`; update with `dotnet tool update Roslynk-cli --global` when requested. Use the target repository's SDK/build instructions as well. Run semantic commands from the target repository or select its solution explicitly.

The CLI and daemon ship together. No manual server executable, MCP configuration or network listener is needed. Normal semantic commands auto-start and reuse the local daemon over UDS on Linux/macOS or current-user-only named pipes on Windows.

## Run from a Roslynk checkout

`dotnet run --project` selects the CLI project; `--solution` after `--` selects the solution to inspect. They can be in different repositories. Replace these example paths with the actual checkout and target:

```sh
cd /path/to/target-repository
dotnet run --project /path/to/Roslynk/src/App/SimCube.Roslynk.Cli/SimCube.Roslynk.Cli.csproj -- diagnostics --solution /path/to/target-repository/App.slnx
```

On Linux/macOS, isolate development from the installed/shared daemon if needed:

```sh
export ROSLYNK_ENDPOINT_DIRECTORY="$(mktemp -d /tmp/roslynk-dev.XXXXXX)"
```

Set this once and retain it for all commands in that development session. A new directory per command defeats daemon reuse. Unix overrides must be real private directories; keep paths short for UDS limits. On Windows, use a private directory and retain the same override for the session.

Rebuilding the CLI does not replace an already-running daemon's executable. To test changed hosting/core code, stop the development daemon using the same endpoint override, then invoke the rebuilt CLI:

```sh
dotnet run --project /path/to/Roslynk/src/App/SimCube.Roslynk.Cli/SimCube.Roslynk.Cli.csproj -- server stop
```

`installer/run.sh` in the Roslynk checkout is an equivalent source-run wrapper. Neither source-running nor semantic querying builds the target solution automatically.

## Unexpected missing references or generated code

If a solution builds but Roslynk reports many CS0012/CS0246 missing types or CS5001 missing entry points, treat workspace inputs as suspect before changing application source. This can arise from missing/stale restore or build outputs, especially project-built source generators and generated test entry points. It is a hypothesis, not a diagnosis from an error code alone.

1. Confirm the selected solution and inspect `roslynk-cli solution status`. `solution open --solution <path>` reports project information and a load-diagnostic count; the count does not expose the underlying messages.
2. From the target repository, inspect `dotnet --info` and its `global.json`. Within the authorized task, restore/build the affected project or solution using its normal settings, for example `dotnet build App.slnx --configuration Debug`. This restores by default. A failed build is useful evidence; do not hide it.
3. Refresh the loaded workspace with `roslynk-cli solution reload --solution /path/to/App.slnx`. External bin/obj changes are ignored by watchers and diagnostics are cached by mode, so compare modes only after refreshing the same workspace.
4. Compare `roslynk-cli diagnostics --solution /path/to/App.slnx --analyzers false` and `--analyzers true` without other intervening changes. Source generators remain active in both modes. If compiler errors clear in both, refreshed inputs were likely responsible. If the difference persists, inspect analyzer/load failures and collect the failing project/diagnostic evidence.

The full solution can report more warnings than a build of one project and its dependencies. Hidden/info counts include findings not printed by default. Roslynk diagnostics are not a build and do not run tests or every MSBuild target.

If SDK selection remains suspect, record the installed SDKs and target configuration. Current registration uses `MSBuildLocator.RegisterDefaults()` once per daemon; it does not guarantee SDK selection from every target solution's directory. A different CLI checkout directory alone is not proof of an SDK mismatch.

## Linux file-watcher limits

An inotify instance/file-descriptor limit error can fault solution loading. Large solutions add watchers; limits are shared with other processes under the same user. Inspect before changing settings:

```sh
cat /proc/sys/fs/inotify/max_user_instances
cat /proc/sys/fs/inotify/max_user_watches
ulimit -n
```

Do not change sysctl values automatically. Explain the evidence and ask for authorization if a machine setting needs changing. Reload the faulted solution after the underlying limit issue is resolved.

## Daemon inspection

`server status` inspects without starting an unavailable daemon. `server start` is idempotent and can auto-start it. `server ping --timing` requires a running daemon and measures request latency. These commands do not load a solution. `solution status` lists all loaded solutions.

Logs are in `daemon.log` under the endpoint directory. By default this is an OS temp directory named `roslynk-<user-hash>`; an endpoint override changes that location. `--timing` writes connection/request timing to stderr. Logs need not contain every captured workspace diagnostic.

`ROSLYNK_IDLE_MINUTES` defaults to 30 for idle solution eviction; zero disables eviction. The daemon remains alive after CLI exit. Ordinary source edits do not need a stop/reload cycle. `server stop` shuts down the shared endpoint and all its loaded workspaces; prefer reloading one solution for workspace freshness.
