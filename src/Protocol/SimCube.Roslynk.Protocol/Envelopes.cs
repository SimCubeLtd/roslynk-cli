using MessagePack;
namespace SimCube.Roslynk.Protocol;

[MessagePackObject]
public sealed record RequestEnvelope([property: Key(0)] uint RequestId, [property: Key(1)] RequestKind Kind, [property: Key(2)] byte[] Payload);
[MessagePackObject]
public sealed record ResponseEnvelope([property: Key(0)] uint RequestId, [property: Key(1)] ResponseStatus Status, [property: Key(2)] byte[] Payload, [property: Key(3)] ProtocolError? Error = null);
public enum ResponseStatus : byte { Success, Error, Incompatible }
[MessagePackObject]
public sealed record ProtocolError([property: Key(0)] string Code, [property: Key(1)] string Message, [property: Key(2)] string[] Candidates, [property: Key(3)] string[]? StaleFiles = null);
[MessagePackObject]
public sealed record HelloRequest([property: Key(0)] int ProtocolVersion, [property: Key(1)] string ClientVersion);
[MessagePackObject]
public sealed record HelloResponse([property: Key(0)] int ProtocolVersion, [property: Key(1)] string ServerVersion);
[MessagePackObject]
public sealed record ServerInfo([property: Key(0)] int ProcessId, [property: Key(1)] string Version, [property: Key(2)] long UptimeMilliseconds);
[MessagePackObject]
public sealed record TextResponse([property: Key(0)] string Text);
[MessagePackObject]
public sealed record EmptyRequest;
[MessagePackObject]
public sealed record SourceVersion([property: Key(0)] string Path, [property: Key(1)] string Version);
