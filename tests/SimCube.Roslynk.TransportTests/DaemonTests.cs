using System.IO;
using SimCube.Roslynk.Cli;
using SimCube.Roslynk.Protocol;
using SimCube.Roslynk.Server;

namespace SimCube.Roslynk.TransportTests;

public sealed class DaemonTests
{
	[Test]
	public async Task WhenMultipleClientsConnect_ThenCorrelationErrorsAndShutdownWork()
	{
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		var endpoint = new LocalEndpoint(root);
		using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		Task daemon = Task.Run(() => Daemon.RunAsync(endpoint, lifetime.Token));
		try
		{
			await WaitForEndpointAsync(endpoint, lifetime.Token);
			await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
			{
				await using IpcClient client = await IpcClient.ConnectAsync(endpoint, false, lifetime.Token);
				ResponseEnvelope response = await client.SendAsync(RequestKind.Ping, new EmptyRequest(), lifetime.Token);
				await Assert.That(response.RequestId).IsEqualTo(2u);
				await Assert.That(Wire.Deserialize<ServerInfo>(response.Payload).ProcessId).IsEqualTo(Environment.ProcessId);
			}));
			await using IpcClient errors = await IpcClient.ConnectAsync(endpoint, false, lifetime.Token);
			ResponseEnvelope invalid = await errors.SendAsync((RequestKind)65535, new EmptyRequest(), lifetime.Token);
			await Assert.That(invalid.Status).IsEqualTo(ResponseStatus.Error); await Assert.That(invalid.Error!.Code).IsEqualTo("Invalid");
			ResponseEnvelope malformed = await errors.SendPayloadAsync(RequestKind.GetSymbol, [0xc1], lifetime.Token);
			await Assert.That(malformed.Error!.Code).IsEqualTo("Invalid");
			await errors.SendAsync(RequestKind.Stop, new EmptyRequest(), lifetime.Token);
			await daemon.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(File.Exists(endpoint.SocketPath)).IsFalse();
		}
		finally
		{
			lifetime.Cancel(); await daemon.WaitAsync(TimeSpan.FromSeconds(10)); Directory.Delete(root, true);
		}
	}

	[Test]
	public async Task WhenProtocolIsIncompatible_ThenSemanticWorkIsRejectedButStopStillWorks()
	{
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		var endpoint = new LocalEndpoint(root);
		using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		Task daemon = Task.Run(() => Daemon.RunAsync(endpoint, lifetime.Token));
		try
		{
			await WaitForEndpointAsync(endpoint, lifetime.Token);
			await using Stream stream = await endpoint.ConnectAsync(lifetime.Token);
			await Wire.WriteAsync(stream, new RequestEnvelope(17, RequestKind.Hello, Wire.Serialize(new HelloRequest(999, "old"))), lifetime.Token);
			ResponseEnvelope hello = (await Wire.ReadAsync<ResponseEnvelope>(stream, lifetime.Token))!;
			await Assert.That(hello.RequestId).IsEqualTo(17u); await Assert.That(hello.Status).IsEqualTo(ResponseStatus.Incompatible);
			await Wire.WriteAsync(stream, new RequestEnvelope(18, RequestKind.GetSolutionStatus, Wire.Serialize(new GetSolutionStatusRequest())), lifetime.Token);
			await Assert.That((await Wire.ReadAsync<ResponseEnvelope>(stream, lifetime.Token))!.Status).IsEqualTo(ResponseStatus.Incompatible);
			await Wire.WriteAsync(stream, new RequestEnvelope(19, RequestKind.Stop, Wire.Serialize(new EmptyRequest())), lifetime.Token);
			await Assert.That((await Wire.ReadAsync<ResponseEnvelope>(stream, lifetime.Token))!.RequestId).IsEqualTo(19u);
			await daemon.WaitAsync(TimeSpan.FromSeconds(10));
		}
		finally { lifetime.Cancel(); await daemon.WaitAsync(TimeSpan.FromSeconds(10)); Directory.Delete(root, true); }
	}

	[Test]
	public async Task WhenAStaleEndpointExists_ThenTheOwnershipHolderReplacesIt()
	{
		if (OperatingSystem.IsWindows()) return;
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		var endpoint = new LocalEndpoint(root); endpoint.EnsurePrivateDirectory();
		File.WriteAllText(endpoint.SocketPath, "stale");
		using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		Task daemon = Task.Run(() => Daemon.RunAsync(endpoint, lifetime.Token));
		try
		{
			await WaitForEndpointAsync(endpoint, lifetime.Token);
			await Assert.That(File.GetUnixFileMode(endpoint.SocketPath)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
			using FileStream owned = endpoint.AcquireLock("startup");
			await Assert.That(() => endpoint.AcquireLock("daemon")).ThrowsExactly<IOException>();
		}
		finally { lifetime.Cancel(); await daemon.WaitAsync(TimeSpan.FromSeconds(10)); Directory.Delete(root, true); }
	}

	[Test]
	public async Task WhenEndpointDirectoryIsPublic_ThenItIsRejected()
	{
		if (OperatingSystem.IsWindows()) return;
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root); File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);
		try { await Assert.That(() => new LocalEndpoint(root).EnsurePrivateDirectory()).ThrowsExactly<IOException>(); }
		finally { Directory.Delete(root); }
	}

	[Test]
	public async Task WhenAnEndpointOrLockIsASymlink_ThenItIsRejected()
	{
		if (OperatingSystem.IsWindows()) return;
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		var endpoint = new LocalEndpoint(root); endpoint.EnsurePrivateDirectory();
		string alias = root + "-alias";
		try
		{
			Directory.CreateSymbolicLink(alias, root);
			await Assert.That(() => new LocalEndpoint(alias).EnsurePrivateDirectory()).ThrowsExactly<IOException>();
			File.CreateSymbolicLink(Path.Combine(root, "daemon.lock"), Path.Combine(root, "missing-target"));
			await Assert.That(() => endpoint.AcquireLock("daemon")).ThrowsExactly<IOException>();
		}
		finally { Directory.Delete(alias); Directory.Delete(root, true); }
	}

	[Test]
	public async Task WhenAnExplicitUnixEndpointPathIsTooLong_ThenTheErrorExplainsTheLimit()
	{
		if (OperatingSystem.IsWindows()) return;
		await Assert.That((await Assert.That(() => new LocalEndpoint(Path.Combine(Path.GetTempPath(), new string('x', 160)))).ThrowsExactly<IOException>())!.Message).Contains("too long");
	}

	[Test]
	public async Task WhenWindowsEndpointPathsDifferOnlyInCase_ThenTheyShareThePipeName()
	{
		if (!OperatingSystem.IsWindows()) return;
		string path = Path.Combine(Path.GetTempPath(), "roslynk-case-test");
		await Assert.That(new LocalEndpoint(path.ToUpperInvariant()).PipeName).IsEqualTo(new LocalEndpoint(path).PipeName);
	}

	internal static async Task WaitForEndpointAsync(LocalEndpoint endpoint, CancellationToken token)
	{
		while (true)
		{
			await using Stream? stream = await IpcClient.TryConnectAsync(endpoint, token);
			if (stream is not null) return;
			await Task.Delay(20, token);
		}
	}
}