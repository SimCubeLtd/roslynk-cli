using System.Buffers;
using System.Buffers.Binary;
using MessagePack;
namespace SimCube.Roslynk.Protocol;

/// <summary>Versioned, bounded MessagePack frames. Length prefixes are unsigned 32-bit big endian.</summary>
public static class Wire
{
	public const int ProtocolVersion = 1;
	public const int MaxFrameLength = 16 * 1024 * 1024;
	public static string ProductVersion => typeof(Wire).Assembly.GetCustomAttributes(false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
	private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
	public static byte[] Serialize<T>(T value) => MessagePackSerializer.Serialize(value, Options);
	public static T Deserialize<T>(ReadOnlyMemory<byte> payload)
	{
		var reader = new MessagePackReader(new ReadOnlySequence<byte>(payload));
		T result = MessagePackSerializer.Deserialize<T>(ref reader, Options);
		if (!reader.End) throw new InvalidDataException("Trailing bytes in MessagePack payload.");
		return result ?? throw new InvalidDataException("Null MessagePack payload.");
	}
	public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken = default)
	{
		byte[] payload = Serialize(value);
		if (payload.Length is 0 or > MaxFrameLength) throw new InvalidDataException("Frame exceeds the 16 MiB limit.");
		byte[] prefix = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)payload.Length);
		await stream.WriteAsync(prefix, cancellationToken);
		await stream.WriteAsync(payload, cancellationToken);
		await stream.FlushAsync(cancellationToken);
	}
	public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken cancellationToken = default) where T : class
	{
		byte[] prefix = new byte[4];
		int first = await stream.ReadAsync(prefix.AsMemory(0, 1), cancellationToken);
		if (first == 0) return null;
		await stream.ReadExactlyAsync(prefix.AsMemory(1), cancellationToken);
		uint length = BinaryPrimitives.ReadUInt32BigEndian(prefix);
		if (length is 0 or > MaxFrameLength) throw new InvalidDataException("Invalid frame length; maximum is 16 MiB.");
		byte[] payload = new byte[(int)length];
		await stream.ReadExactlyAsync(payload, cancellationToken);
		return Deserialize<T>(payload);
	}
}
