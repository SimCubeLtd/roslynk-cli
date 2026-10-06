using System.Diagnostics;
using SimCube.Roslynk.Protocol;

if (args.Length != 2)
{
	Console.Error.WriteLine("Usage: dotnet run --project src/Benchmarks/SimCube.Roslynk.Benchmarks -- <roslynk-cli-executable> <solution-path>");
	return 2;
}
string executable = Path.GetFullPath(args[0]);
string solution = Path.GetFullPath(args[1]);
string directory = Path.Combine(Path.GetTempPath(), "rk-bench-" + Guid.NewGuid().ToString("N"));
var endpoint = new LocalEndpoint(directory);
try
{
	Sample cold = await RunAsync("diagnostics", "--solution", solution, "--timing");
	Console.WriteLine($"cold_diagnostics_ms={cold.Milliseconds:F3} {cold.Error.Trim()}");
	Console.WriteLine((await RunAsync("server", "status")).Output.Trim());
	await MeasureProcessesAsync("cli_startup", ["--version"]);
	await MeasureProcessesAsync("cli_ping", ["server", "ping", "--timing"]);
	await MeasureProcessesAsync("warm_diagnostics", ["diagnostics", "--solution", solution, "--timing"]);

	// Warm serializer/JIT paths before measuring them in this process.
	var request = new GetSymbolRequest(solution, "SimpleLibrary.Greeter");
	byte[] payload = Wire.Serialize(request);
	for (int i = 0; i < 100; i++) { Wire.Serialize(request); Wire.Deserialize<GetSymbolRequest>(payload); }
	var watch = Stopwatch.StartNew();
	for (int i = 0; i < 10_000; i++) Wire.Serialize(request);
	Console.WriteLine($"request_serialize_us={watch.Elapsed.TotalMicroseconds / 10_000:F3}");
	watch.Restart();
	for (int i = 0; i < 10_000; i++) Wire.Deserialize<GetSymbolRequest>(payload);
	Console.WriteLine($"request_deserialize_us={watch.Elapsed.TotalMicroseconds / 10_000:F3}");
	var response = new TextResponse("errors=0\nwarnings=0\n");
	watch.Restart();
	for (int i = 0; i < 10_000; i++) Wire.Serialize(response);
	Console.WriteLine($"response_serialize_us={watch.Elapsed.TotalMicroseconds / 10_000:F3}");

	var connections = new List<double>();
	for (int i = 0; i < 31; i++)
	{
		watch.Restart();
		await using Stream stream = await endpoint.ConnectAsync(CancellationToken.None);
		await HelloAsync(stream);
		if (i > 0) connections.Add(watch.Elapsed.TotalMilliseconds);
	}
	Console.WriteLine($"connect_and_hello_median_ms={Median(connections):F3}");
	await using (Stream stream = await endpoint.ConnectAsync(CancellationToken.None))
	{
		await HelloAsync(stream);
		var pings = new List<double>();
		for (uint id = 2; id < 1032; id++)
		{
			watch.Restart();
			await Wire.WriteAsync(stream, new RequestEnvelope(id, RequestKind.Ping, Wire.Serialize(new EmptyRequest())));
			ResponseEnvelope result = await Wire.ReadAsync<ResponseEnvelope>(stream) ?? throw new IOException("Daemon disconnected.");
			if (result.RequestId != id || result.Status != ResponseStatus.Success) throw new IOException("Ping failed.");
			if (id >= 32) pings.Add(watch.Elapsed.TotalMilliseconds);
		}
		Console.WriteLine($"warm_ipc_ping_median_ms={Median(pings):F3} p95_ms={pings.Order().ElementAt(949):F3}");
	}
	Sample listeners = await RunAsync("server", "status");
	Console.WriteLine(listeners.Output.Trim());
}
finally
{
	await RunAsync("server", "stop");
	if (Directory.Exists(directory)) Directory.Delete(directory, true);
}
return 0;

async Task MeasureProcessesAsync(string label, string[] commands)
{
	await RunAsync(commands);
	var samples = new List<Sample>();
	for (int i = 0; i < 20; i++) samples.Add(await RunAsync(commands));
	Console.WriteLine($"{label}_median_ms={Median(samples.Select(sample => sample.Milliseconds)):F3} p95_ms={samples.Select(sample => sample.Milliseconds).Order().ElementAt(18):F3}");
	if (samples[0].Error.Length > 0) Console.WriteLine($"{label}_sample {samples[0].Error.Trim()}");
}
async Task<Sample> RunAsync(params string[] commands)
{
	var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
	foreach (string command in commands) start.ArgumentList.Add(command);
	start.Environment["ROSLYNK_ENDPOINT_DIRECTORY"] = directory;
	using Process process = Process.Start(start) ?? throw new IOException("Could not launch CLI.");
	var watch = Stopwatch.StartNew();
	Task<string> stdout = process.StandardOutput.ReadToEndAsync();
	Task<string> stderr = process.StandardError.ReadToEndAsync();
	try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
	catch { process.Kill(entireProcessTree: true); throw; }
	string output = await stdout; string error = await stderr;
	if (process.ExitCode != 0 && !(commands[0] == "diagnostics" && process.ExitCode == 1 && output.Contains("errors="))) throw new IOException(error);
	return new(watch.Elapsed.TotalMilliseconds, output, error);
}
static async Task HelloAsync(Stream stream)
{
	await Wire.WriteAsync(stream, new RequestEnvelope(1, RequestKind.Hello, Wire.Serialize(new HelloRequest(Wire.ProtocolVersion, Wire.ProductVersion))));
	ResponseEnvelope hello = await Wire.ReadAsync<ResponseEnvelope>(stream) ?? throw new IOException("No hello response.");
	if (hello.Status != ResponseStatus.Success) throw new IOException(hello.Error?.Message);
}
static double Median(IEnumerable<double> samples)
{
	double[] sorted = samples.Order().ToArray();
	int middle = sorted.Length / 2;
	return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
}
sealed record Sample(double Milliseconds, string Output, string Error);
