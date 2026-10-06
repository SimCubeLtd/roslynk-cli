using System.Buffers.Binary;
using SimCube.Roslynk.Protocol;

namespace SimCube.Roslynk.TransportTests;

public sealed class ProtocolTests
{
	[Test]
	public async Task WhenContractsAreSerialized_ThenEnvelopeCorrelationAndTypedBatchSurvive()
	{
		var request = new MultiQueryRequest("/fixture.slnx", [new GetSymbolRequest("/fixture.slnx", "Fixture.Widget"), new FindReferencesRequest("/fixture.slnx", "Fixture.Widget.Run", 7)]);
		var envelope = new RequestEnvelope(123, RequestKind.MultiQuery, Wire.Serialize(request));
		RequestEnvelope decoded = Wire.Deserialize<RequestEnvelope>(Wire.Serialize(envelope));
		await Assert.That(decoded.RequestId).IsEqualTo(123u);
		await Assert.That(decoded.Kind).IsEqualTo(RequestKind.MultiQuery);
		MultiQueryRequest batch = Wire.Deserialize<MultiQueryRequest>(decoded.Payload);
		await Assert.That((await Assert.That(batch.Operations[0]).IsTypeOf<GetSymbolRequest>())!.SymbolName).IsEqualTo("Fixture.Widget");
		await Assert.That((await Assert.That(batch.Operations[1]).IsTypeOf<FindReferencesRequest>())!.MaxResults).IsEqualTo(7);
		var response = new ResponseEnvelope(123, ResponseStatus.Error, [], new("Stale", "changed", ["candidate"]));
		await Assert.That(Wire.Deserialize<ResponseEnvelope>(Wire.Serialize(response)).Error! with { Candidates = response.Error!.Candidates }).IsEqualTo(response.Error);
	}

	[Test]
	public async Task WhenReadsAreFragmented_ThenFramesAreReadExactlyAndRemainSeparate()
	{
		using var memory = new MemoryStream();
		var one = new RequestEnvelope(1, RequestKind.Ping, Wire.Serialize(new EmptyRequest()));
		await Wire.WriteAsync(memory, one);
		await Wire.WriteAsync(memory, one with { RequestId = 2 });
		byte[] bytes = memory.ToArray();
		await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes)).IsEqualTo((uint)Wire.Serialize(one).Length);
		await using var fragments = new FragmentedStream(bytes);
		await Assert.That((await Wire.ReadAsync<RequestEnvelope>(fragments))!.RequestId).IsEqualTo(1u);
		await Assert.That((await Wire.ReadAsync<RequestEnvelope>(fragments))!.RequestId).IsEqualTo(2u);
		await Assert.That(await Wire.ReadAsync<RequestEnvelope>(fragments)).IsNull();
	}

	[Test]
	[Arguments(0u)]
	[Arguments(uint.MaxValue)]
	[Arguments((uint)Wire.MaxFrameLength + 1)]
	public async Task WhenLengthIsInvalid_ThenItIsRejectedBeforeAllocatingTheBody(uint length)
	{
		byte[] prefix = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(prefix, length);
		await Assert.That(() => Wire.ReadAsync<RequestEnvelope>(new MemoryStream(prefix))).ThrowsExactly<InvalidDataException>();
	}

	[Test]
	[Arguments(new byte[] { 0, 0 })]
	[Arguments(new byte[] { 0, 0, 0, 4, 1 })]
	public async Task WhenAFrameIsTruncated_ThenItFails(byte[] bytes) =>
	 await Assert.That(() => Wire.ReadAsync<RequestEnvelope>(new FragmentedStream(bytes))).ThrowsExactly<EndOfStreamException>();

	[Test]
	public async Task WhenMessagePackIsMalformed_ThenItFails()
	{
		await Assert.That(() => Wire.ReadAsync<RequestEnvelope>(new MemoryStream([0, 0, 0, 1, 0xc1]))).ThrowsExactly<MessagePack.MessagePackSerializationException>();
		await Assert.That(() => Wire.Deserialize<EmptyRequest>(new byte[] { 0x90, 0 })).ThrowsExactly<InvalidDataException>();
	}

	[Test]
	public async Task WhenReadIsCancelled_ThenCancellationPropagates()
	{
		using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
		await Assert.That(() => Wire.ReadAsync<RequestEnvelope>(new MemoryStream([0]), cancelled.Token)).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task WhenAResponseHasTheWrongCorrelation_ThenTheClientRejectsIt()
	{
		using var bytes = new MemoryStream();
		await Wire.WriteAsync(bytes, new ResponseEnvelope(999, ResponseStatus.Success, Wire.Serialize(new ServerInfo(1, "version", 0))));
		await using var client = new SimCube.Roslynk.Cli.IpcClient(new ResponseStream(bytes.ToArray()));
		await Assert.That(async () => { await client.SendAsync(RequestKind.Ping, new EmptyRequest(), CancellationToken.None); }).ThrowsExactly<InvalidDataException>();
	}
	private sealed class ResponseStream(byte[] bytes) : MemoryStream(bytes)
	{
		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
	}

	private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
	{
		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
	}
}