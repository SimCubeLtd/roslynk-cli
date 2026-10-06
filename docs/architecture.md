# Architecture

```text
Agent or human
    ↓ command arguments / plain text
SimCube.Roslynk.Cli
    ↓ MessagePack envelopes and typed payloads
Unix domain socket / Windows named pipe
    ↓
SimCube.Roslynk.Server
    ↓ owned application requests/results
SimCube.Roslynk.Core
    ↓ internal Roslyn objects
Roslyn / MSBuildWorkspace
```

## Responsibilities and dependencies

Core contains the existing semantic feature operations, normalized solution registry, immutable snapshots, project/workspace loading, analyzer/code-action caches, conditional projections, Razor mapping, file watching and write pipeline. All Roslyn-bearing types are internal. Its public application facade, `RoslynkApplication`, accepts owned typed requests and returns `OperationResult`/`ApplicationError`. Source bodies and established outlines remain owned text results; Core does not write to stdout/stderr. DI registration is the other public entry point. Core references no MessagePack, CLI, socket/pipe, HTTP or MCP assembly.

Protocol is independent of Core and owns integer-key MessagePack DTOs, numeric request kinds, envelopes, framing and local endpoint primitives. It contains no Roslyn models or algorithms. Request classes deliberately differ from domain classes; Server explicitly maps between them. `IQueryRequest` is a closed MessagePack union of the 14 snapshot-query types, not a dictionary/object protocol.

Server owns IPC listeners, connection lifetime/cancellation, hello validation, response correlation and exception mapping. It delegates every semantic request to Core. Hosting, operation instrumentation and maintenance belong here. Never put symbol resolution, diagnostics or refactor algorithms in dispatch code.

CLI owns System.CommandLine parsing/help, solution discovery, auto-start, response output and exit codes. It references Protocol and the packaged server entry point. It uses no Roslyn API or application service directly. One executable ships both roles: a private `--daemon` entry point invokes the Server assembly. This keeps global .NET tool installation atomic and CLI/daemon versions together. Normal commands never initialize a workspace in the CLI process.

The [migration matrix](capability-migration.md) accounts for all 28 original capabilities. Boundary and capability tests protect exported APIs and explicit mappings. Internal operation names retain historical identifiers for the pinned-query catalog; they are not MCP adapters.

## Framing and serialization

Each frame has a four-byte unsigned big-endian payload length, then exactly that many MessagePack bytes. Length must be between 1 and 16 MiB. Readers use asynchronous exact-length reads, tolerate fragmented reads, distinguish clean EOF before a frame from truncation inside it, and reject invalid lengths before allocation. Decoders use MessagePack's UntrustedData security settings and reject trailing payload bytes. Numeric keys and union tags are fixed protocol contracts.

`RequestEnvelope` carries a uint32 correlation ID, ushort `RequestKind` and binary typed payload. `ResponseEnvelope` echoes the ID and carries Success/Error/Incompatible, a binary result payload and an optional `ProtocolError` with code, message, candidates and stale paths. Semantic results use a typed `TextResponse`; lifecycle results use `ServerInfo`. Clients validate correlation before accepting a response. Logging never enters the IPC stream.

Version 1 request kinds: Hello=1, Ping=2, ServerStatus=3, Stop=4. Semantic kinds 10 through 37 cover the complete original inventory, explicitly assigned in `RequestKind.cs`. Values must not be recycled. Batch requests contain typed query unions; the Core boundary maps them into the established internal pinned-model binder. That binder's JSON scalar representation is an internal implementation detail, not a wire protocol or required agent input. Internal code-action IDs also remain opaque strings.

## Negotiation and compatibility

Hello is the first frame on every connection. It sends protocol version and product informational version; the daemon returns both. Product versions include the build revision, so different installed builds cannot silently reuse an incompatible daemon. The client rejects mismatches immediately with an actionable stop/retry message. An incompatible connection may only issue Stop using the stable lifecycle envelope. Protocol version changes are required when changing framing/envelope or payload interpretation incompatibly; extend numbered fields/tags deliberately and test both edges. This is a local product protocol, not a distributed compatibility framework.

## Endpoint ownership and lifecycle

Default endpoints use a deterministic hash of user/domain/profile under the OS temp directory. Unix uses a short `daemon.sock` path; Windows uses a hashed named pipe with CurrentUserOnly on both endpoints. `ROSLYNK_ENDPOINT_DIRECTORY` provides isolated instances for tests or an alternate short path.

Unix directory creation uses mode 0700 and rejects symlink leaves or existing broader permissions. Socket permissions are 0600. Lock files live inside the private directory and reject symlinks. An open FileShare.None handle holds an OS sharing lock; handles release on crash. The daemon holds `daemon.lock` for its lifetime. Startup uses a separate `startup.lock`, rechecks endpoint availability while holding it, and only launches when no daemon holds ownership. The server alone, while owning the lock, removes a stale socket. Competing daemon processes cannot unlink the active owner's endpoint.

CLI startup has a 30-second bound and probes IPC with short cancellation windows. Unix spawning uses nohup/backgrounding with stdout/stderr redirected to the private log. Windows launches a hidden independent process and redirects daemon console logging to the log. The server is included in the tool; it never asks users to locate a DLL. Normal unavailable-daemon requests auto-start; explicit status only reports availability.

Stop replies, closes the accept loop, cancels client operations, awaits connections/maintenance, removes the socket and disposes hosting/Core state. CLI stop waits for ownership release. Host application lifetime handles process termination. Crash leaves the ownership handle released and possibly a stale socket; the next semantic command starts a replacement. Multiple loaded solutions are retained independently. Idle maintenance defaults to 30 minutes; it only evicts while the application has no active operations, and can be disabled with `ROSLYNK_IDLE_MINUTES=0`.

## Concurrency and cancellation

Connections run concurrently, with one outstanding request per client connection. A read-ahead frame task detects EOF/disconnect during semantic execution and cancels that connection's active operation. Unexpected pipelining is rejected. Exact reads and writes accept cancellation tokens. Initial/shared workspace loads keep their own lifetime; cancelling a waiting client does not cancel another client's load. Shared analyzer computations retain their existing bounded cache/cancellation policy.

Core captures one immutable model per logical read. Existing fenced reads wait for publication under the short read lock, then release it before semantic computation. Each RoslynInstance retains its single-consumer write/diagnostics channel. Writes and rebuild publications remain ordered; stale rebasing and disk validation remain under that writer. Different documents/solutions can be queried concurrently. No global execution lock is added. The brief maintenance counter lock protects eviction, not semantic work.

Cancellation is passed into existing operations and persistence; the application does not detach an active write merely because its caller stops waiting. Once a persistence operation completes it publishes the corresponding model through the existing writer. Atomic file replacements and best-effort batch rollback retain their prior guarantees and limitations.

## Security assumptions

The trusted boundary is the current OS user. Other users cannot traverse a correctly owned Unix private directory or connect to a CurrentUserOnly Windows pipe. A pre-created inaccessible directory fails safely rather than being chmodded or trusted. The current user can inspect/replace their own daemon and source files; administrator/root compromise is outside this design. There are no bearer tokens or unnecessary TCP listeners. Custom endpoint paths must remain private. Symlink checks protect endpoint leaves/locks; they are not a general sandbox for arbitrary filesystem paths.

All frames are bounded and deserialized without typeless/runtime-type activation. Request kinds and payload models are explicit. Domain validation stays in Core; malformed wire inputs become transport errors. A local same-user process can consume daemon resources, so the protocol does not claim isolation from malicious same-user software.

## Observability and verification

Core retains its ActivitySource/Meter, load durations and open-solution observations under `SimCube.Roslynk.Core`. Server spans identify numeric operations by enum name. The former bundled OTLP exporter is intentionally removed because it offers HTTP/gRPC export paths. Core instrumentation remains OpenTelemetry-compatible and observable through external .NET diagnostics tools. There is no HTTP instrumentation or MCP session tracking. CLI `--timing` measures connection/hello and request round trip; server lifecycle commands report total elapsed time. `server ping` isolates IPC from workspace work.

Tests cover original semantic behavior, public boundaries, all capability contracts, MessagePack round trips/framing, malformed/oversized/truncated input, correlation, incompatible negotiation, concurrent clients/startup, stale sockets, disconnect cancellation, help/discovery/errors and external-process semantic edits/reuse/crash recovery. Linux executes UDS end to end. The named-pipe connection handler is also tested on Linux, while Windows-specific CurrentUserOnly semantics and macOS UDS execution require platform runs.
