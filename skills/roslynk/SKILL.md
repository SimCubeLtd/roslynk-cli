---
name: roslynk
description: Use the Roslynk CLI for semantic C#, Razor and CSHTML queries, diagnostics and refactors when the user explicitly requests Roslynk or this skill.
disable-model-invocation: true
---

# Roslynk

Use this skill only when the user explicitly invokes it or asks to use Roslynk. Once requested, use Roslynk throughout that task for semantic inspection and authorized refactors. Invocation does not authorize unrelated edits or machine configuration changes.

Prefer compiler-backed queries over text search for C# symbols and relationships. Use ordinary file tools for configuration, documentation, new files and unsupported edits. Roslynk supports loaded .NET solutions, including C# regions in Razor and CSHTML.

## Start with the target solution

Run commands from the target repository, not the Roslynk checkout. The CLI discovers `.slnx` and `.sln` files by walking upward, including ancestor `src` and `Source` directories. If discovery is ambiguous or the working directory differs, pass `--solution /absolute/path/App.slnx` consistently. Multiple solutions can stay loaded at once.

```sh
roslynk-cli --help
roslynk-cli diagnostics --solution /path/to/App.slnx
roslynk-cli refs --help
```

Requires the .NET 10 SDK and `roslynk-cli` on PATH. Installation is `dotnet tool install Roslynk-cli --global`. If unavailable, report that and install only within the user's authorized scope. For a source checkout, see [running and troubleshooting](references/operations.md), including `dotnet run` against another repository.

The first semantic command starts the local daemon and waits for solution readiness. Later commands reuse warm workspaces. Do not start, stop or reload it for each query. On a fresh checkout, restore and build the target solution before relying on semantic results, especially for generated code and project-built generators. Workspace loading is a design-time load, not a substitute for a build.

## Choose the smallest useful query

| Need | Commands |
| --- | --- |
| Find a declaration by partial name | `search` |
| Resolve a symbol, inspect its members or read its complete source | `symbol`, `members`, `body` |
| Resolve a source position or inspect its binding/type/nullability | `definition`, `expression` |
| Find usages, value reads/writes or callers | `refs`, `reads`, `writes`, `callers` |
| Find implementations, overrides or inheritance | `implementations`, `hierarchy` |
| Check compiler/analyzer findings | `diagnostics`, alias `diag` |
| Assess unused members or inactive conditional code | `dead-code`, `dead-conditionals` |
| Rename a symbol or parameter, append an optional parameter | `rename`, `rename-parameter`, `change-signature` |
| Extract code or remove unused usings | `extract`, `usings` |
| Discover/apply a code action or diagnostic fix | `actions`, `apply-action`, `fix` |
| Apply a unified diff to existing files | `patch` |
| Combine related reads on one snapshot | `batch` |
| Inspect/manage loaded solutions or the daemon | `solution open/status/reload`, `server status/start/ping/stop` |

Run `roslynk-cli <command> --help` for arguments and defaults. Read the relevant section of [the command reference](references/tools.md) for scope, output and limits. Do not load the entire reference or all help pages for a simple query.

Use fully qualified names. Quote signatures containing parentheses, spaces or shell metacharacters, for example `'MyApp.Widget.Run(int)'`. Local functions include their enclosing member, such as `'N.T.Method(int).local(string)'`. Parameter read/write identities use `'N.T.Method:parameter'`. Copy exact `candidate=` values from ambiguous responses rather than guessing an overload.

Source lines and columns are 1-based. Existing paths resolve from the current directory; otherwise they are solution-relative. Prefer absolute paths when context is uncertain. Razor positions refer to the original `.razor` or `.cshtml` file, not generated C#.

```sh
roslynk-cli search OrderService --max-results 10
roslynk-cli members MyApp.OrderService
roslynk-cli body 'MyApp.OrderService.Process(Order)'
roslynk-cli definition src/Orders.cs 42 13
roslynk-cli batch 'refs MyApp.OrderService.Process' 'callers MyApp.OrderService.Process'
```

`batch` accepts 14 read-only query kinds, up to 25 operations on one immutable snapshot. Diagnostics, actions, writes and lifecycle commands are excluded. Check each slot for failure/truncation. For a follow-up batch requiring the same publication, pass its returned ID with `--expect-snapshot`; if rejected, gather a fresh batch rather than combining generations. A 200,000-character budget can omit later slots. Single-query result limits can also truncate results; increase supported `--max-results` values deliberately and report incomplete coverage.

## Interpret diagnostics and output

Plain text is the default. stdout contains successful results, including diagnostic findings; stderr contains operation failures, ambiguity candidates and requested timings. Headers use `key=value`, outlines are tab-indented, bodies are verbatim. `--json` returns one object for scripting, and `--timing` writes timings to stderr.

Exit codes are 0 for success, 1 for application/connection failures or compiler errors reported by diagnostics, 2 for invalid arguments, and 130 for cancellation. On exit 1, inspect both streams; valid compiler findings are not a transport failure.

Diagnostics always return error/warning/info/hidden counts. Analyzers are enabled by default; detail flags do not change the counts. For counts only:

```sh
roslynk-cli diagnostics --errors false --warnings false --info false --hidden false
```

Use `--analyzers false` to isolate compiler diagnostics when investigating analyzer discrepancies. Source generators still run. Hidden findings and warnings are not necessarily build failures. If diagnostics unexpectedly report missing assemblies, missing generated test symbols or missing entry points despite a successful build, follow [workspace troubleshooting](references/operations.md) before proposing source changes.

## Make semantic changes

Inspect the target and its relationships before a broad change. Preview with `--check-only`, review the affected files, then perform the authorized edit and re-query diagnostics/references. Run the repository's relevant build/tests for behavioral validation; semantic diagnostics do not replace them.

```sh
roslynk-cli rename 'MyApp.Widget.Run(int)' Execute --check-only
roslynk-cli rename 'MyApp.Widget.Run(int)' Execute
roslynk-cli actions src/Widget.cs 12 5
roslynk-cli fix src/Widget.cs CS0219 12 5 --check-only
```

All write commands support `--check-only`. `usings` is a mutation unless previewed. Dead-code reports are advisory and delete nothing. Preserve declared intent when applying a fix; do not remove interfaces, base types or declarations simply to silence an error. Code actions return opaque IDs. Several distinct fixes can return Conflict with candidate IDs; inspect/choose the intended action and rediscover after source changes.

On Stale or Conflict, inspect disk/source changes and obtain a fresh snapshot before recomputing the edit. Do not blindly retry an old action or diff. Semantic writes reject intervening changes to touched documents and stale disk text. Patches without `--base-version PATH=SHA256` apply against current disk text and require uniquely matching hunks. Patching supports existing text files, not creation/deletion; plain-file fallback is confined to the solution directory and excludes bin/obj. File replacements are individually atomic with best-effort batch rollback, not a filesystem transaction.

Rename covers conditional branches, linked files and Razor references. Conditional querying toggles individual uniformly defined/undefined symbols; it does not enumerate every symbol combination. Absence from a query is not proof that code can never execute. Razor edits must map back to source; generated `.g.cs` files are never persisted. Analyzer IDE/CA fixes are unavailable in Razor. Read the command reference for extraction, imports and mapping limits.

## Handle lifecycle and failures

Watchers fold ordinary C# changes and trigger lazy rebuilds for build/generator/Razor inputs. Re-query after edits. If results remain stale after a real build or restored dependencies, use `solution reload --solution <path>` as a troubleshooting step. bin/obj changes are ignored by watchers, so external build outputs alone need not refresh the warm workspace.

For Indexing/Building, inspect `solution status` and allow loading to finish. For Faulted, inspect the failure and load status before retrying. For NotFound/Ambiguous, check the solution, name and candidates. Invalid/NotSupported needs a corrected request or supported approach. Truncated needs narrower queries or an explicit incomplete-results report.

Use `server status` and `server ping --timing` to inspect the daemon without loading a solution. After a tool upgrade, `server stop` can stop an incompatible daemon and the next semantic command starts the installed version. Restarting affects all loaded solutions on that endpoint. The daemon stops by itself when idle eviction (30 minutes by default) closes its last loaded solution; the next semantic command starts it again and reloads the solution. See [running and troubleshooting](references/operations.md) for logs, isolated development daemons and Linux watcher limits.
