using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCube.Roslynk.Core;
using SimCube.Roslynk.Core.Application;
using SimCube.Roslynk.Protocol;

namespace SimCube.Roslynk.Server;

public static class Daemon
{
	private static readonly ActivitySource Activities = new("SimCube.Roslynk.Server");
	private static readonly TimeSpan MinimumSweep = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan MaximumSweep = TimeSpan.FromMinutes(1);

	public static Task RunAsync(LocalEndpoint endpoint, CancellationToken cancellationToken = default) =>
		RunAsync(endpoint, ConfiguredIdleWindow(), cancellationToken);

	/// <summary><paramref name="idleFor"/> is how long a solution may go unused before eviction; null disables idle maintenance.</summary>
	internal static async Task RunAsync(LocalEndpoint endpoint, TimeSpan? idleFor, CancellationToken cancellationToken)
	{
		using FileStream ownership = endpoint.AcquireLock("daemon");
		HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
		builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
		builder.Services.AddRoslynk();
		using IHost host = builder.Build();
		await host.StartAsync(cancellationToken);
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
		RoslynkApplication application = host.Services.GetRequiredService<RoslynkApplication>();
		var dispatcher = new RequestDispatcher(application);
		Task maintenance = MaintainAsync(application, idleFor, lifetime);
		var clients = new ConcurrentDictionary<long, Task>();
		long nextClient = 0;
		var stopwatch = Stopwatch.StartNew();
		Socket? listener = null;
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				// Only the daemon ownership-lock holder may unlink a stale endpoint.
				File.Delete(endpoint.SocketPath);
				listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				listener.Bind(new UnixDomainSocketEndPoint(endpoint.SocketPath));
				File.SetUnixFileMode(endpoint.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				listener.Listen(128);
			}
			while (!lifetime.IsCancellationRequested)
			{
				Stream stream;
				if (OperatingSystem.IsWindows())
				{
					var pipe = new NamedPipeServerStream(endpoint.PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
					try { await pipe.WaitForConnectionAsync(lifetime.Token); stream = pipe; }
					catch { await pipe.DisposeAsync(); throw; }
				}
				else stream = new NetworkStream(await listener!.AcceptAsync(lifetime.Token), ownsSocket: true);
				long id = Interlocked.Increment(ref nextClient);
				Task task = ServeAsync(stream, dispatcher.DispatchAsync, lifetime, stopwatch);
				clients[id] = task;
				_ = task.ContinueWith(_ => clients.TryRemove(id, out Task? removed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
			}
		}
		catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
		finally
		{
			lifetime.Cancel();
			listener?.Dispose();
			await Task.WhenAll(clients.Values);
			await maintenance;
			if (!OperatingSystem.IsWindows()) File.Delete(endpoint.SocketPath);
			await host.StopAsync(CancellationToken.None);
		}
	}

	private static TimeSpan? ConfiguredIdleWindow()
	{
		double minutes = double.TryParse(Environment.GetEnvironmentVariable("ROSLYNK_IDLE_MINUTES"), System.Globalization.CultureInfo.InvariantCulture, out double configured) ? configured : 30;
		return minutes <= 0 || !double.IsFinite(minutes) ? null : TimeSpan.FromMinutes(minutes);
	}

	/// <summary>
	/// Evicts idle solutions, and stops the daemon once eviction has closed the last one: with nothing loaded it
	/// holds no warm state, and the next command starts a fresh daemon. A daemon that never loaded a solution
	/// keeps running, as does one whose solutions were all closed some other way.
	/// </summary>
	private static async Task MaintainAsync(RoslynkApplication application, TimeSpan? idleFor, CancellationTokenSource lifetime)
	{
		if (idleFor is not TimeSpan window) return;
		// A tiny positive ROSLYNK_IDLE_MINUTES rounds to a window PeriodicTimer rejects as a period, so the
		// sweep has a floor. The window itself is left alone: it just means "evict as soon as it is unused".
		TimeSpan sweep = TimeSpan.FromTicks(Math.Clamp(window.Ticks, MinimumSweep.Ticks, MaximumSweep.Ticks));
		using var timer = new PeriodicTimer(sweep);
		try
		{
			while (await timer.WaitForNextTickAsync(lifetime.Token))
			{
				application.EvictIdle(window, DateTime.UtcNow, out bool closedLastSolution);
				if (!closedLastSolution) continue;
				lifetime.Cancel();
				return;
			}
		}
		catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
	}

	internal static async Task ServeAsync(Stream stream, Func<RequestEnvelope, CancellationToken, Task<ResponseEnvelope>> dispatch, CancellationTokenSource lifetime, Stopwatch stopwatch)
	{
		await using (stream)
		using (var connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
		{
			Task<RequestEnvelope?> pending = Wire.ReadAsync<RequestEnvelope>(stream, connection.Token);
			Task<ResponseEnvelope>? activeOperation = null;
			try
			{
				RequestEnvelope? helloEnvelope = await pending;
				if (helloEnvelope is null) return;
				if (helloEnvelope.Kind != RequestKind.Hello) throw new InvalidDataException("Hello must be the first request.");
				HelloRequest hello = Wire.Deserialize<HelloRequest>(helloEnvelope.Payload);
				bool compatible = hello.ProtocolVersion == Wire.ProtocolVersion && hello.ClientVersion == Wire.ProductVersion;
				await Wire.WriteAsync(stream, new ResponseEnvelope(helloEnvelope.RequestId,
				 compatible ? ResponseStatus.Success : ResponseStatus.Incompatible,
				 Wire.Serialize(new HelloResponse(Wire.ProtocolVersion, Wire.ProductVersion)),
				 compatible ? null : new ProtocolError("Incompatible", "CLI and daemon versions differ. Run 'roslynk-cli server stop', then retry.", [])), connection.Token);


				pending = Wire.ReadAsync<RequestEnvelope>(stream, connection.Token);
				while (await pending is RequestEnvelope envelope)
				{
					// Read ahead detects disconnect while semantic work is active. Each client sends one outstanding request.
					pending = Wire.ReadAsync<RequestEnvelope>(stream, connection.Token);
					using Activity? activity = Activities.StartActivity(envelope.Kind.ToString());
					Task<ResponseEnvelope> operation = activeOperation = !compatible && envelope.Kind != RequestKind.Stop
					 ? Task.FromResult(new ResponseEnvelope(envelope.RequestId, ResponseStatus.Incompatible, [], new("Incompatible", "Only server stop is available for an incompatible daemon.", [])))
					 : RespondAsync(envelope, dispatch, stopwatch, connection.Token);
					Task completed = await Task.WhenAny(operation, pending);
					if (completed == pending)
					{
						RequestEnvelope? next = await pending;
						connection.Cancel();
						try { await operation; } catch (OperationCanceledException) { }
						if (next is not null) throw new InvalidDataException("Only one outstanding request per connection is supported.");
						return;
					}
					ResponseEnvelope response = await operation;
					await Wire.WriteAsync(stream, response, connection.Token);
					if (envelope.Kind == RequestKind.Stop) { lifetime.Cancel(); return; }
				}
			}
			catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or MessagePack.MessagePackSerializationException or ArgumentException)
			{
				if (exception is not OperationCanceledException) Console.Error.WriteLine($"IPC: {exception.Message}");
			}
			finally
			{
				connection.Cancel();
				if (activeOperation is not null)
					try { await activeOperation; } catch (OperationCanceledException) { }
				try { await pending; } catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or MessagePack.MessagePackSerializationException) { }
			}
		}
	}

	private static async Task<ResponseEnvelope> RespondAsync(RequestEnvelope envelope, Func<RequestEnvelope, CancellationToken, Task<ResponseEnvelope>> dispatch, Stopwatch stopwatch, CancellationToken cancellationToken)
	{
		try
		{
			if (envelope.Kind is RequestKind.Ping or RequestKind.ServerStatus or RequestKind.Stop)
			{
				Wire.Deserialize<EmptyRequest>(envelope.Payload);
				return new(envelope.RequestId, ResponseStatus.Success, Wire.Serialize(new ServerInfo(Environment.ProcessId, Wire.ProductVersion, stopwatch.ElapsedMilliseconds)));
			}
			return await dispatch(envelope, cancellationToken);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception exception)
		{
			string code = exception is ArgumentException or InvalidDataException or MessagePack.MessagePackSerializationException ? "Invalid" : "Faulted";
			return new(envelope.RequestId, ResponseStatus.Error, [], new(code, exception.Message, []));
		}
	}
}
