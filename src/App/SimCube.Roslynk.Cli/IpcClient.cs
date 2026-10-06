using System.Diagnostics;
using System.Net.Sockets;
using SimCube.Roslynk.Protocol;

namespace SimCube.Roslynk.Cli;

internal sealed class IpcClient(Stream stream) : IAsyncDisposable
{
	private uint NextId;
	public static async Task<IpcClient> ConnectAsync(LocalEndpoint endpoint, bool autoStart, CancellationToken cancellationToken)
	{
		Stream? stream = await TryConnectAsync(endpoint, cancellationToken);
		if (stream is null && autoStart)
		{
			using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			startup.CancelAfter(TimeSpan.FromSeconds(30));
			try
			{
				FileStream? startupLock = null;
				while (startupLock is null)
				{
					try { startupLock = endpoint.AcquireLock("startup"); }
					catch (IOException) { await Task.Delay(50, startup.Token); }
				}
				using (startupLock)
				{
					stream = await TryConnectAsync(endpoint, startup.Token);
					if (stream is null)
					{
						bool daemonStarting;
						try { using FileStream probe = endpoint.AcquireLock("daemon"); daemonStarting = false; }
						catch (IOException) { daemonStarting = true; }
						if (!daemonStarting) StartDetached(endpoint);
						while (stream is null)
						{
							await Task.Delay(50, startup.Token);
							stream = await TryConnectAsync(endpoint, startup.Token);
						}
					}
				}
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				throw new TimeoutException($"Daemon startup timed out. See '{endpoint.LogPath}'.");
			}
		}
		if (stream is null) throw new IOException("Roslynk daemon is not running.");
		var client = new IpcClient(stream);
		try
		{
			ResponseEnvelope hello = await client.SendAsync(RequestKind.Hello, new HelloRequest(Wire.ProtocolVersion, Wire.ProductVersion), cancellationToken);
			if (hello.Status == ResponseStatus.Incompatible)
				throw new InvalidOperationException(hello.Error!.Message);
			if (hello.Status != ResponseStatus.Success) throw new InvalidDataException(hello.Error?.Message ?? "Daemon rejected handshake.");
			HelloResponse version = Wire.Deserialize<HelloResponse>(hello.Payload);
			if (version.ProtocolVersion != Wire.ProtocolVersion || version.ServerVersion != Wire.ProductVersion) throw new InvalidDataException("Unexpected daemon version.");
			return client;
		}
		catch { await client.DisposeAsync(); throw; }
	}

	internal static async Task<Stream?> TryConnectAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
	{
		endpoint.EnsurePrivateDirectory();
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeout.CancelAfter(150);
		try { return await endpoint.ConnectAsync(timeout.Token); }
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
		catch (Exception exception) when (exception is SocketException or IOException) { return null; }
	}

	public Task<ResponseEnvelope> SendAsync<T>(RequestKind kind, T request, CancellationToken cancellationToken) => SendPayloadAsync(kind, Wire.Serialize(request), cancellationToken);
	public async Task<ResponseEnvelope> SendPayloadAsync(RequestKind kind, byte[] payload, CancellationToken cancellationToken)
	{
		uint id = ++NextId;
		await Wire.WriteAsync(stream, new RequestEnvelope(id, kind, payload), cancellationToken);
		ResponseEnvelope response = await Wire.ReadAsync<ResponseEnvelope>(stream, cancellationToken) ?? throw new EndOfStreamException("Daemon disconnected.");
		if (response.RequestId != id) throw new InvalidDataException("Response correlation ID does not match the request.");
		return response;
	}

	private static void StartDetached(LocalEndpoint endpoint)
	{
		string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the dotnet host.");
		string? assembly = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
		 ? typeof(CliApplication).Assembly.Location : null;
		ProcessStartInfo start;
		if (OperatingSystem.IsWindows())
		{
			start = new(executable) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = AppContext.BaseDirectory };
			if (assembly is not null) start.ArgumentList.Add(assembly);
			start.ArgumentList.Add("--daemon");
			start.ArgumentList.Add(endpoint.DirectoryPath);
		}
		else
		{
			start = new("/bin/sh") {
				UseShellExecute = false,
				WorkingDirectory = AppContext.BaseDirectory,
				ArgumentList = { "-c", "nohup \"$0\" ${2:+\"$2\"} --daemon >>\"$1\" 2>&1 </dev/null &", executable, endpoint.LogPath, assembly ?? "" }
			};
		}
		if (!OperatingSystem.IsWindows()) start.Environment["ROSLYNK_ENDPOINT_DIRECTORY"] = endpoint.DirectoryPath;
		using Process process = Process.Start(start) ?? throw new IOException("Could not launch daemon.");
	}
	public ValueTask DisposeAsync() => stream.DisposeAsync();
}
