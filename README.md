# Roslynk

Roslynk provides compiler-backed C# queries and safe refactors from a small CLI. A shared local daemon keeps Roslyn workspaces, compilations and analyzer caches warm between commands. It supports C#, Razor and CSHTML in loaded .NET solutions.

## Origin and attribution

This project is derived from [Roslynk](https://github.com/mrpmorris/Roslynk), created by [Peter Morris (@mrpmorris)](https://github.com/mrpmorris). Peter Morris and the upstream contributors built the original MCP application and the Roslyn semantic engine, workspace management, queries, refactorings and tests on which this version is based.

SimCube's changes replace the MCP transport with a CLI and local daemon while preserving the upstream semantic functionality. The `SimCube` assembly names identify these changes; credit for the original Roslynk belongs to its upstream authors. Roslynk remains MIT-licensed, and the original **Copyright (c) 2026 Peter Morris** notice is retained in [LICENCE](LICENCE).

## Installation

Requires the .NET 10 SDK on `PATH`. The NuGet package is `Roslynk-cli`; the installed command is `roslynk-cli`. Install the CLI and daemon together:

```sh
dotnet tool install Roslynk-cli --global
roslynk-cli --help
```

Run commands from your repository:

```sh
roslynk-cli diagnostics
roslynk-cli refs MyApp.OrderService.Process
roslynk-cli callers MyApp.OrderService.Process
roslynk-cli symbol MyApp.OrderService
roslynk-cli implementations MyApp.IOrderService
roslynk-cli rename MyApp.OrderService.Process Execute --check-only
roslynk-cli rename MyApp.OrderService.Process Execute
```

Default output is compact plain text. Query results retain Roslynk's `key=value` headers and tab-indented outlines; source bodies remain verbatim. Diagnostics show counts and error/warning details by default. Errors and candidates go to stderr. Exit codes are `0` for success, `1` for application/connection failures or compiler errors reported by diagnostics, `2` for invalid CLI arguments/requests, and `130` for cancellation. `--json` produces one JSON object for successful output, never a stream; text responses contain a `Text` field. `--timing` writes timings to stderr.

Run `roslynk-cli <command> --help` for arguments, switches and defaults. See the [command reference](skills/roslynk/references/tools.md) and [capability migration](docs/capability-migration.md).

## Solutions

The CLI walks up from the current directory looking for `.slnx` or `.sln` files. It also checks an ancestor's `src` or `Source` directory, common repository layouts. One candidate is selected automatically; multiple candidates produce an error listing the paths. Choose explicitly when needed:

```sh
roslynk-cli diagnostics --solution src/MyApp.slnx
```

Solution paths identify workspaces. The daemon can keep several solutions loaded simultaneously; there are no opaque solution IDs to remember. Semantic commands wait for initial loading and reuse an already-loaded instance. File watchers fold C# edits or trigger a lazy rebuild for build/generator/Razor inputs.

```sh
roslynk-cli solution open --solution src/MyApp.slnx
roslynk-cli solution status
roslynk-cli solution reload --solution src/MyApp.slnx
```

`solution open` starts loading in the background. Reload is an explicit troubleshooting operation; ordinary edits do not require it.

## Commands and editing

Navigation includes `symbol`, `body`, `members`, `search`, `definition`, `expression`, `refs`, `reads`, `writes`, `callers`, `implementations` and `hierarchy`. Diagnostics and analysis include `diagnostics`/`diag`, `dead-code` and `dead-conditionals`. Editing includes `rename`, `rename-parameter`, `change-signature`, `extract`, `usings`, `actions`, `apply-action`, `fix` and `patch`.

`body` reads source declared in the solution. For a symbol that only exists in a referenced assembly, such as a NuGet package or the BCL, pass `--decompile` to get C# reconstructed from its IL, marked `source=decompiled`. It never happens implicitly and is not available in `batch`.

Positions are 1-based. Existing source paths are resolved from the current directory; otherwise paths are interpreted relative to the solution directory. Use fully-qualified symbol names; include parameter types to select an overload. Local functions use the enclosing member's name, such as `N.T.Method(int).local(string)`. Copy exact candidate names from ambiguous results.

```sh
roslynk-cli actions src/Widget.cs 12 5
roslynk-cli fix src/Widget.cs CS0219 12 5 --check-only
roslynk-cli batch 'refs N.T.Method' 'callers N.T.Method' 'hierarchy N.T'
roslynk-cli patch changes.diff --check-only
roslynk-cli patch changes.diff --base-version 'src/Widget.cs=SHA256'
```

Read batches use one immutable snapshot and preserve continuation checks through `--expect-snapshot`. Writes offer `--check-only` previews. Semantic edits reject intervening edits to touched documents and changed disk text. File replacement is individually atomic with best-effort batch rollback; it is not a filesystem transaction. Patch hunks must match uniquely; without a base version, patches are computed against current disk text. Existing text files are supported, not file creation/deletion. See the reference for Razor limits and code-action conflicts.

## Daemon

The first semantic command automatically starts the daemon. Later CLI processes connect to the same daemon and exit after printing their response. Multiple clients share warm state; per-solution writer queues serialize mutations and rebuild publication while immutable reads can run concurrently.

```sh
roslynk-cli server status
roslynk-cli server start
roslynk-cli server ping --timing
roslynk-cli server stop
```

The daemon uses MessagePack with bounded length-prefixed frames over a Unix domain socket on Linux/macOS or a current-user-only named pipe on Windows. It creates no TCP or HTTP listener. Unix endpoint directories have permissions `0700`, and sockets have `0600`. Startup and ownership locks prevent competing clients from launching multiple active daemons; stale sockets are replaced by the ownership holder.

CLI and server versions must match. After upgrading, `roslynk-cli server stop` also works against an incompatible daemon; the next command starts the installed version. The CLI locates the bundled server itself.

Logs go to `daemon.log` in the private endpoint directory, normally a `roslynk-<user-hash>` directory under the OS temp directory. `ROSLYNK_ENDPOINT_DIRECTORY` can select an isolated short endpoint directory for tests or separate daemon instances. Unix overrides must be real private directories. `ROSLYNK_IDLE_MINUTES` defaults to 30 and disables idle solution eviction when set to zero. Active operations are protected from eviction. When eviction closes the last loaded solution the daemon process stops too, and the next semantic command starts a new one. A daemon that never loaded a solution keeps running until `roslynk-cli server stop`.

Core retains OpenTelemetry-compatible `ActivitySource`/`Meter` instrumentation, and Server emits operation spans. The bundled OTLP exporter is removed to keep HTTP/gRPC export paths out of the application. Use external .NET diagnostics tooling to observe instrumentation.

Linux UDS and the packaged tool are exercised by integration tests. macOS uses the same UDS implementation, and Windows uses the named-pipe branch; those operating systems need their own execution checks. See [architecture](docs/architecture.md).

## Agents

Copy the entire [skills/roslynk](skills/roslynk) directory, including `agents/openai.yaml` and `references`, into `~/.agents/skills/roslynk` for Codex or `.agents/skills/roslynk` for one repository. For Claude Code, use `~/.claude/skills/roslynk` or `.claude/skills/roslynk`.

The skill is explicit-only. Invoke it in Codex with `$roslynk`, for example `Use $roslynk to find callers of MyApp.Widget.Run`, or in Claude Code with `/roslynk`. Codex uses `policy.allow_implicit_invocation: false` in `agents/openai.yaml`; Claude Code uses `disable-model-invocation: true` in `SKILL.md`. These settings control skill activation, not shell permissions or authorization to edit files. See [OpenAI skill metadata](https://learn.chatgpt.com/docs/build-skills#optional-metadata) and [Claude Code invocation control](https://code.claude.com/docs/en/skills#control-who-invokes-a-skill).

The main skill covers command selection, solution discovery, batching, diagnostics, semantic editing and error handling. Detailed syntax stays in the [command reference](skills/roslynk/references/tools.md); [running and troubleshooting](skills/roslynk/references/operations.md) covers source execution, restore/build prerequisites, workspace freshness and daemon management. On a fresh target checkout, restore and build before relying on semantic results, especially when generated code is involved. After rebuilding external outputs, reload a stale workspace with `roslynk-cli solution reload --solution <path>`.

No schemas or MCP integration are needed.

## Development

```sh
dotnet build SimCube.Roslynk.slnx --configuration Release
dotnet test --solution SimCube.Roslynk.slnx --configuration Release
./installer/run.sh refs MyApp.OrderService.Process --solution /path/to/MyApp.slnx
dotnet pack src/App/SimCube.Roslynk.Cli/SimCube.Roslynk.Cli.csproj --configuration Release
```

Tests use TUnit on Microsoft.Testing.Platform, selected by `global.json`. Run one project with `dotnet test --project <path>` or filter with `--treenode-filter "/*/*/*ApplyPipelineTests/*"`. Core tests limit concurrent workspace owners to eight to bound memory use. TUnit includes coverage and TRX reporting; use `--coverage --report-trx` when needed.

Publishing a GitHub release runs Release tests on GitHub's hosted `ubuntu-latest` runner, packs the `Roslynk-cli` global tool, pushes it to NuGet.org using OIDC trusted publishing through `NuGet/login@v1`, and attaches the `.nupkg` to that release after a successful push. The release tag is the package version: use a numeric version without a `v` prefix, such as `2.1.0` or `2.1.0-beta.1`. Published prereleases are included; draft releases and tag pushes do not trigger publishing. The NuGet trusted publishing policy must allow the `SimCubeLtd/roslynk-cli` repository and `workflow.yml` for the `SimCube` NuGet profile. The workflow requests a temporary publishing key immediately before pushing; no stored API key or GitHub secret is required. See [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

Validate workflow changes locally with `actionlint`; no custom runner configuration is required.

Core, Protocol, Server and CLI assemblies use the `SimCube` prefix. The former HTTP host, stdio bridge, MCP registration/schema adapters, Windows service hosting and Aspire AppHost are removed.
