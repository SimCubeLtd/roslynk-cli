using System.IO.Pipes;
using System.Diagnostics;
using SimCube.Roslynk.Protocol;
using SimCube.Roslynk.Server;

namespace SimCube.Roslynk.TransportTests;

public sealed class DisconnectTests
{
	[Test]
	public async Task WhenAClientDisconnectsDuringWork_ThenItsCancellationReachesTheOperation()
	{
		string name = "rk-test-" + Guid.NewGuid().ToString("N");
		using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var listener = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		Task accept = listener.WaitForConnectionAsync(lifetime.Token);
		var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
		await client.ConnectAsync(lifetime.Token); await accept;
		Task connection = Daemon.ServeAsync(listener, async (request, token) =>
		{
			entered.TrySetResult();
			try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
			catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
			return new(request.RequestId, ResponseStatus.Success, []);
		}, lifetime, Stopwatch.StartNew());
		await Wire.WriteAsync(client, new RequestEnvelope(1, RequestKind.Hello, Wire.Serialize(new HelloRequest(Wire.ProtocolVersion, Wire.ProductVersion))), lifetime.Token);
		await Assert.That((await Wire.ReadAsync<ResponseEnvelope>(client, lifetime.Token))!.Status).IsEqualTo(ResponseStatus.Success);
		await Wire.WriteAsync(client, new RequestEnvelope(2, RequestKind.GetSymbol, []), lifetime.Token);
		await entered.Task.WaitAsync(lifetime.Token);
		await client.DisposeAsync();
		await cancelled.Task.WaitAsync(lifetime.Token);
		await connection.WaitAsync(lifetime.Token);
		await Assert.That(lifetime.IsCancellationRequested).IsFalse();
	}
}