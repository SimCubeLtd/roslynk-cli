# Capability migration

All 28 former MCP capabilities are preserved through typed `RoslynkApplication` methods. Their implementation operations and Roslyn-bearing helpers are internal to Core. Server and CLI use explicit DTO mappings; no reflection or dictionary dispatch protocol is used.

| Original MCP capability | Core API | CLI command | Status |
| --- | --- | --- | --- |
| `get_callers` | `RoslynkApplication.GetCallersAsync` | `callers` | Preserved |
| `apply_code_action` | `RoslynkApplication.ApplyCodeActionAsync` | `apply-action` | Preserved |
| `apply_code_fix` | `RoslynkApplication.ApplyCodeFixAsync` | `fix` | Preserved |
| `get_code_actions` | `RoslynkApplication.GetCodeActionsAsync` | `actions` | Preserved |
| `find_dead_conditionals` | `RoslynkApplication.FindDeadConditionalsAsync` | `dead-conditionals` | Preserved |
| `find_dead_code` | `RoslynkApplication.FindDeadCodeAsync` | `dead-code` | Preserved |
| `get_diagnostics` | `RoslynkApplication.GetDiagnosticsAsync` | `diagnostics` | Preserved |
| `multi_query` | `RoslynkApplication.MultiQueryAsync` | `batch` | Preserved |
| `apply_patch` | `RoslynkApplication.ApplyPatchAsync` | `patch` | Preserved |
| `extract_method` | `RoslynkApplication.ExtractMethodAsync` | `extract` | Preserved |
| `find_reads` | `RoslynkApplication.FindReadsAsync` | `reads` | Preserved |
| `find_references` | `RoslynkApplication.FindReferencesAsync` | `refs` | Preserved |
| `find_writes` | `RoslynkApplication.FindWritesAsync` | `writes` | Preserved |
| `rename_symbol` | `RoslynkApplication.RenameSymbolAsync` | `rename` | Preserved |
| `change_signature` | `RoslynkApplication.ChangeSignatureAsync` | `change-signature` | Preserved |
| `rename_parameter` | `RoslynkApplication.RenameParameterAsync` | `rename-parameter` | Preserved |
| `get_solution_status` | `RoslynkApplication.GetSolutionStatusAsync` | `solution status` | Preserved |
| `open_solution` | `RoslynkApplication.OpenSolutionAsync` | `solution open` | Preserved |
| `reload_solution` | `RoslynkApplication.ReloadSolutionAsync` | `solution reload` | Preserved |
| `find_definition` | `RoslynkApplication.FindDefinitionAsync` | `definition` | Preserved |
| `find_implementations` | `RoslynkApplication.FindImplementationsAsync` | `implementations` | Preserved |
| `get_expression_info` | `RoslynkApplication.GetExpressionInfoAsync` | `expression` | Preserved |
| `get_members` | `RoslynkApplication.GetMembersAsync` | `members` | Preserved |
| `get_symbol` | `RoslynkApplication.GetSymbolAsync` | `symbol` | Preserved |
| `get_symbol_body` | `RoslynkApplication.GetSymbolBodyAsync` | `body` | Preserved; explicit `--decompile` added for referenced assemblies (not in batch) |
| `get_type_hierarchy` | `RoslynkApplication.GetTypeHierarchyAsync` | `hierarchy` | Preserved |
| `search_symbols` | `RoslynkApplication.SearchSymbolsAsync` | `search` | Preserved |
| `remove_unused_usings` | `RoslynkApplication.RemoveUnusedUsingsAsync` | `usings` | Preserved |

Intentionally removed: MCP schema publication/session handling/tool registration, JSON-RPC and stdio forwarding, HTTP endpoints, Windows Service Control Manager hosting Aspire HTTP orchestration and the bundled HTTP/gRPC OTLP exporter. These were transport/hosting mechanisms. No Roslyn capability is intentionally removed.

The compact semantic outline and source body format is preserved as an application-owned text result. Owned error DTOs separately carry code, message, candidates and stale files. Core does not expose Roslyn objects. This avoids rewriting tested semantic rendering during the transport migration.
