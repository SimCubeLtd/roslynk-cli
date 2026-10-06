# Verification and latency

The migration preserves all 28 original semantic capabilities. The original 480 engine tests remain, with three new application boundary/default/reload tests. Transport coverage adds 31 cases across protocol, CLI, capability inventory, endpoint security, daemon operation, disconnect handling and external-process integration.

## Executed checks

```sh
dotnet build SimCube.Roslynk.slnx --configuration Release
dotnet test --solution SimCube.Roslynk.slnx --configuration Release --no-build --no-restore
dotnet format whitespace SimCube.Roslynk.slnx --no-restore --include <changed C# files>
dotnet pack src/App/SimCube.Roslynk.Cli/SimCube.Roslynk.Cli.csproj --configuration Release --output <temporary package directory>
dotnet tool install Roslynk-cli --tool-path <isolated tool directory> --add-source <temporary package directory> --version 0.0.1-local
dotnet run --project src/Benchmarks/SimCube.Roslynk.Benchmarks --configuration Release -- <installed roslynk-cli path> tests/TestFixtures/SimpleSolution/SimpleSolution.slnx
```

Focused lifecycle, batch, application-boundary, CLI, security and external-process tests also passed during implementation. Semantic diagnostics through the CLI against `SimCube.Roslynk.slnx` reported errors=0, warnings=0, infos=0 and hidden=74 using compiler-only mode.

The external-process fixture creates its own solution/source, restores it, races five launchers and confirms one daemon PID. It exercises diagnostics, references, callers, symbols, implementations, source bodies, source paths from a nested working directory and pinned batches. It verifies rename preview writes nothing, actual rename updates disk and immediate semantic queries, and errors use stderr/meaningful exit codes. It kills only its isolated daemon to verify stale-socket crash recovery, then verifies SIGTERM cleanup on Unix. The installed .NET tool also executed a real fixture reference query successfully, proving bundled server and MSBuild build-host assets are available.

Protocol tests cover MessagePack DTO/union round trips, big-endian framing, fragmented reads, multiple frames, clean EOF, truncated/malformed/oversized input, cancellation, correlation mismatch and error responses. Daemon tests cover multiple clients, unknown kinds, malformed payloads, version rejection with compatible lifecycle stop, stale socket replacement, owner-only permissions, symlink rejection and endpoint path limits. A controlled named-pipe operation confirms disconnect cancellation reaches the active operation without stopping other clients. The public Core boundary rejects Roslyn/transport type leaks. The complete original capability inventory checks typed Core/Protocol contract coverage and defaults.

`ss -ltnp` and `ss -lxnp` were inspected for a running isolated packaged daemon: its application listener was a Unix domain socket, with no TCP listener. The .NET runtime also exposes its ordinary local diagnostics socket. Direct/transitive package inspection found no MCP, ASP.NET Core, gRPC or OTLP packages. No HTTP/JSON-RPC/stdio bridge or Rust/FFI implementation remains in production source. Normal CLI operations use plain text, not JSON.

## Measured Linux latency

Measured on 5 October 2026 with .NET SDK 10.0.112/runtime 10.0.12 and the installed portable Release tool. The SimpleSolution fixture contains a deliberate compiler diagnostic; diagnostics exit 1 is expected and unrelated to command transport failure. All warm samples used the same daemon PID. These are local observations, not cross-machine guarantees.

| Measurement | Median / average | p95 |
| --- | ---: | ---: |
| CLI process startup and `--version`, 20 samples | 41.849 ms median | 42.894 ms |
| Full CLI `server ping`, 20 samples | 86.041 ms median | 87.750 ms |
| Full CLI cached diagnostics, 20 samples | 91.044 ms median | 93.636 ms |
| Direct IPC connect + hello, 30 samples | 0.077 ms median | Not recorded |
| Warm persistent-connection IPC ping, 1,000 samples | 0.026 ms median | 0.036 ms |
| Serialize a small symbol request, 10,000 samples | 1.488 µs average | Not recorded |
| Deserialize that request, 10,000 samples | 0.565 µs average | Not recorded |
| Serialize a 20-character text response, 10,000 samples | 0.173 µs average | Not recorded |

One cold auto-start plus diagnostics run took 2,989.562 ms. The CLI's timings separated 255.05 ms for connection/startup and 2,677.88 ms for the request/workspace load. A warm diagnostics sample reported 38.58 ms for CLI connection/hello and 4.26 ms for request round trip. Process/JIT/serializer initialization dominate the short-lived CLI; the warm daemon's IPC overhead is small. Server operation spans and existing Core instrumentation allow further attribution without adding a transport framework. The benchmark source is included in the solution and uses isolated endpoints.

## Limits of this verification

Linux UDS, real daemon processes, the packaged tool, startup/reuse/concurrent calls, crash recovery and graceful SIGTERM shutdown were executed. The shared named-pipe connection handler was exercised on Linux. Windows-specific pipe ACLs/hidden-process launch and macOS UDS execution were not run here; their implementations and platform-conditional tests need execution on those operating systems. Unix default endpoint paths fall back to a short `/tmp` path when an OS temp path is too long; explicit oversized paths are rejected. Windows endpoint hashing normalizes path case.

Builds pass with the repository's existing Microsoft.NET.StringTools 18.10.1 SDK compatibility warning: that package's build targets recommend net11 while this repository targets net10. Runtime MSBuild assemblies still come from MSBuildLocator's SDK, and all semantic tests pass. No warnings or analyzers were disabled to hide this warning.
