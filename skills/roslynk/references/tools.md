# CLI command reference

Run `roslynk-cli --help` or `roslynk-cli <command> --help` for discoverable syntax. All commands accept `--solution <path>`, `--json` and `--timing`. Solution paths are discovered when omitted by walking upward and checking ancestor `src` and `Source` directories; ambiguous matches require `--solution`. Booleans accept explicit `true`/`false`; source positions are 1-based. Existing source paths are resolved from the current directory, otherwise relative to the solution directory. Defaults below are CLI defaults.

Successful output is plain text with `key=value` headers and tab-indented outlines. Booleans in results are `Y`/`N`. Source bodies are returned verbatim. `--json` returns a single object with a `Text` field, or server information fields for lifecycle commands. Errors use stderr with code/message, candidates and stale paths. Exit codes: 0 success, 1 application/IPC failure or diagnostic compiler errors, 2 invalid arguments, 130 cancellation.

For installation, source execution, build prerequisites and stale-workspace diagnosis, see [running and troubleshooting](operations.md).

## server

`roslynk-cli server status|start|ping|stop`

Status and ping require an existing daemon and do not start it. Start can auto-start; none of these commands load a solution. Stop shuts down all workspaces on the endpoint and waits for ownership release. Use `--timing` with ping to measure overhead. Batch availability: no.

## callers

`roslynk-cli callers <method-name>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## apply-action

`roslynk-cli apply-action <action-id>`

| Option | Default |
| --- | --- |
| `--check-only` | `false` |

Batch availability: no.

## fix

`roslynk-cli fix <document-path> <diagnostic-id> <line> <column>`

| Option | Default |
| --- | --- |
| `--check-only` | `false` |

Batch availability: no.

## actions

`roslynk-cli actions <document-path> <line> <column>`

| Option | Default |
| --- | --- |
| `--end-line` | `null` |
| `--end-column` | `null` |

Batch availability: no.

## dead-conditionals

`roslynk-cli dead-conditionals`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## dead-code

`roslynk-cli dead-code`

| Option | Default |
| --- | --- |
| `--scope` | `null` |
| `--public` | `false` |
| `--max-results` | `50` |

Batch availability: yes.

## diagnostics

`roslynk-cli diagnostics`

| Option | Default |
| --- | --- |
| `--errors` | `true` |
| `--warnings` | `true` |
| `--info` | `false` |
| `--hidden` | `false` |
| `--analyzers` | `true` |

Batch availability: no.

## batch

`roslynk-cli batch 'refs N.T.M' 'callers N.T.M' [--expect-snapshot <id>]`

Quoted command strings use the same positional arguments and switches as individual queries. The outer solution is used for every slot. Read-only operations below are eligible; writes, diagnostics, actions and lifecycle operations are rejected. One model is captured for the whole batch. Up to 25 operations run, with a 200,000-character accumulated response budget checked between slots. Whole remaining slots report Truncated, and one completed slot may exceed the budget. Responses include snapshot and random body boundaries. Continuations reject mismatched publication generations before executing.

## patch

`roslynk-cli patch <file|-> [--base-version PATH=SHA256] [--check-only]`

Read a git unified diff from a file or stdin. Repeat `--base-version` for individual disk version guards. Without guards, hunks apply to current disk text; each must match uniquely. Existing text files only, no creation/deletion/binary patches. Linked model paths are supported. Plain-file fallback is inside the solution directory and rejects bin/obj. BOM detection preserves recognized UTF-8/UTF-16 encodings. Successful source patches update every matching model document; arbitrary plain files remain disk-only. Razor/build inputs can trigger watcher rebuilds.

## extract

`roslynk-cli extract <document-path> <start-line> <start-column> <end-line> <end-column>`

| Option | Default |
| --- | --- |
| `--method-name` | `null` |
| `--local-function` | `false` |
| `--check-only` | `false` |

Batch availability: no.

## reads

`roslynk-cli reads <symbol-name>`

| Option | Default |
| --- | --- |
| `--max-results` | `100` |

Batch availability: yes.

## refs

`roslynk-cli refs <symbol-name>`

| Option | Default |
| --- | --- |
| `--max-results` | `100` |

Batch availability: yes.

## writes

`roslynk-cli writes <symbol-name>`

| Option | Default |
| --- | --- |
| `--max-results` | `100` |

Batch availability: yes.

## rename

`roslynk-cli rename <symbol-name> <new-name>`

| Option | Default |
| --- | --- |
| `--check-only` | `false` |

Batch availability: no.

## change-signature

`roslynk-cli change-signature <method-id> <parameter-type> <parameter-name> <default-value>`

| Option | Default |
| --- | --- |
| `--call-site-argument` | `null` |
| `--check-only` | `false` |

Batch availability: no.

## rename-parameter

`roslynk-cli rename-parameter <method-id> <parameter-name> <new-name>`

| Option | Default |
| --- | --- |
| `--check-only` | `false` |

Batch availability: no.

## solution status

`roslynk-cli solution status`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: no.

## solution open

`roslynk-cli solution open`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: no.

## solution reload

`roslynk-cli solution reload`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: no.

## definition

`roslynk-cli definition <file-path> <line> <column>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## implementations

`roslynk-cli implementations <symbol-name>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## expression

`roslynk-cli expression <file-path> <line> <column>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## members

`roslynk-cli members <type-name>`

| Option | Default |
| --- | --- |
| `--inherited` | `false` |
| `--filter` | `null` |
| `--include-methods` | `true` |
| `--include-fields` | `true` |
| `--include-properties` | `true` |
| `--include-events` | `true` |
| `--include-nested-types` | `true` |

Batch availability: yes.

## symbol

`roslynk-cli symbol <symbol-name>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## body

`roslynk-cli body <symbol-name>`

| Option | Default |
| --- | --- |
| `--leading-trivia` | `false` |

Batch availability: yes.

## hierarchy

`roslynk-cli hierarchy <type-name>`

| Option | Default |
| --- | --- |
| None beyond global options | |

Batch availability: yes.

## search

`roslynk-cli search <query>`

| Option | Default |
| --- | --- |
| `--max-results` | `50` |

Batch availability: yes.

## usings

`roslynk-cli usings`

| Option | Default |
| --- | --- |
| `--document-path` | `null` |
| `--check-only` | `false` |

Batch availability: no.

## Shared semantic contracts

- Symbol names are fully qualified. Optional parameter-type lists choose overloads. Local functions use their enclosing member name; container overloads can also carry signatures. Candidate names round-trip into the same command.
- Queries cover the base compilation and single-symbol conditional projections. Mixed-definition symbols are skipped, and all possible multi-symbol combinations are not enumerated. Matching signatures across projects can collapse into one semantic identity.
- `reads`/`writes` accept fields, properties and parameters, with parameters written `N.T.Method:parameter`. Access tags are read, assign, compound, increment, ref, out and init. Compound/increment/ref appear in both results.
- `symbol` reports a declaration or metadata identity. `body` preserves full declarations and original source formatting. `expression` reports compiler binding, types/conversions, nullable flow, constants, source origin and documentation; unavailable facts are none.
- `diagnostics`/`diag` always report error/warning/info/hidden counts; switches select details, and analyzers are enabled by default. Set `--errors false --warnings false` for counts-only output. Diagnostics drain preceding writes and map positions against the solution actually compiled. Private fixer-trigger IDs are hidden.
- `actions` returns opaque action IDs. `apply-action` rediscovers the action. `fix` requires the diagnostic ID and exact source position. Several distinct fixes return Conflict with candidate action IDs and write nothing; choose an action rather than silently changing declared intent.
- `rename` preserves conditional branches, linked/multi-target physical paths and Razor references. `rename-parameter` includes named arguments and the override/interface family.
- `change-signature` appends one optional parameter to an ordinary method and threads a call-site argument. `extract` uses Roslyn extraction and rejects unsafe or non-compiling results. No document creation/removal is performed by semantic writes.
- All write commands support `--check-only`; preview publishes/writes nothing. Actual writes rebase onto the latest snapshot, preserve intervening changes to untouched files, reject touched-document conflicts and reject stale disk text. Generated .g.cs paths are never persisted. Individual replacements are atomic with best-effort batch rollback, not a filesystem transaction. Semantic pipeline encoding defaults differ from patch BOM preservation.
- `dead-code` reports confidence/reasons and deletes nothing. `dead-conditionals` reports branches absent from loaded configuration coverage.

## Razor and CSHTML

Position-based commands accept `.razor`/`.cshtml` paths and positions in their C# regions. Generated positions are round-trip checked; edits fold back into source regions with original indentation. Rename writes Razor additional documents while retaining generated C# in memory. Analyzer IDE/CA fixes are unavailable in Razor, and imports files are never trimmed by unused-using removal. In CSHTML where a new method cannot be added, use `extract --local-function`. Unmappable changes are rejected. Generated code can be retained when generators are unavailable, so its presence alone does not guarantee freshness.

## Failures and lifecycle

Application codes include Indexing, Faulted, NotFound, Ambiguous, NotSupported, Stale, Invalid, Conflict and Truncated. Semantic CLI commands normally await initial readiness; solution open reports Building immediately. Source/watch changes can update the publication generation without changing text. Re-read after Stale/Conflict. Query limits report truncation explicitly.

`solution open` and `solution reload` use the discovered or explicit path; `solution status` lists all loaded paths and progress without discovering a solution. Reload is an explicit backstop, not part of ordinary editing.

`server status`, `start`, `stop` and `ping` do not discover/load solutions. Semantic commands auto-start the daemon. Stop can negotiate with an incompatible daemon and waits for ownership release; the next command starts the installed version.
