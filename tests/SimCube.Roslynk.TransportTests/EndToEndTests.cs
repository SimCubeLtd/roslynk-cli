using System.IO;
using System.Diagnostics;
using SimCube.Roslynk.Cli;
using SimCube.Roslynk.Protocol;

namespace SimCube.Roslynk.TransportTests;

public sealed class EndToEndTests
{
	[Test]
	public async Task WhenRealCliProcessesQueryAndRename_ThenTheyAutoStartAndReuseTheWarmDaemon()
	{
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		string ipc = Path.Combine(root, "ipc");
		Directory.CreateDirectory(root);
		string solution = Path.Combine(root, "Fixture.slnx");
		string source = Path.Combine(root, "Widget.cs");
		await File.WriteAllTextAsync(Path.Combine(root, "Fixture.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
		await File.WriteAllTextAsync(solution, "<Solution><Project Path=\"Fixture.csproj\" /></Solution>");
		await File.WriteAllTextAsync(source, "namespace Fixture;\npublic interface IWorker { void Run(); }\npublic class Widget : IWorker { public void Run() { } }\npublic class Caller { public void Use() { new Widget().Run(); } }\n");
		try
		{
			Result restore = await ProcessAsync(root, ipc, "dotnet", ["restore", solution, "--verbosity", "quiet"]);
			await Assert.That(restore.Code).IsEqualTo(0);
			// Race real CLI launchers, rather than bypassing startup with an in-process server.
			Result[] first = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CliAsync(root, ipc, "server", "start")));
			foreach (Result result in first)
				await Assert.That(result.Code).IsEqualTo(0).Because(result.Error);
			string pid = first[0].Output.Split(' ').Single(part => part.StartsWith("pid="));
			foreach (Result result in first)
				await Assert.That(result.Output).Contains(pid);
			Result diag = await CliAsync(root, ipc, "diagnostics", "--analyzers", "false");
			await Assert.That(diag.Code == 0).IsTrue(); await Assert.That(diag.Output).Contains("errors=0"); await Assert.That(diag.Error).IsEmpty();
			Result[] queries = await Task.WhenAll(
			 CliAsync(root, ipc, "refs", "Fixture.Widget.Run"),
			 CliAsync(root, ipc, "callers", "Fixture.Widget.Run"),
			 CliAsync(root, ipc, "symbol", "Fixture.Widget"),
			 CliAsync(root, ipc, "implementations", "Fixture.IWorker"));
			foreach (Result result in queries)
				await Assert.That(result.Code).IsEqualTo(0).Because(result.Error);
			await Assert.That(queries[0].Output).Contains("Widget.cs"); await Assert.That(queries[1].Output).Contains("Use"); await Assert.That(queries[2].Output).Contains("class Widget"); await Assert.That(queries[3].Output).Contains("Widget");
			Result body = await CliAsync(root, ipc, "body", "Fixture.Widget");
			await Assert.That(body.Code == 0).IsTrue();
			await Assert.That(body.Output).EndsWith("public class Widget : IWorker { public void Run() { } }\n");
			string nestedDirectory = Path.Combine(root, "Nested"); Directory.CreateDirectory(nestedDirectory);
			int callColumn = (await File.ReadAllLinesAsync(source))[3].IndexOf(".Run", StringComparison.Ordinal) + 2;
			Result definition = await CliAsync(nestedDirectory, ipc, "definition", "../Widget.cs", "4", callColumn.ToString(System.Globalization.CultureInfo.InvariantCulture));
			await Assert.That(definition.Code == 0).IsTrue(); await Assert.That(definition.Output).Contains("Run");
			Result batch = await CliAsync(root, ipc, "batch", "refs Fixture.Widget.Run", "callers Fixture.Widget.Run");
			await Assert.That(batch.Code == 0).IsTrue(); await Assert.That(batch.Output).Contains("operations=2"); await Assert.That(batch.Output).Contains("snapshot=");
			Result members = await CliAsync(root, ipc, "batch", "members Fixture.Widget");
			await Assert.That(members.Code == 0).IsTrue();
			await Assert.That(members.Output).Contains("method,Run");
			Result preview = await CliAsync(root, ipc, "rename", "Fixture.Widget.Run", "Execute", "--check-only");
			await Assert.That(preview.Code == 0).IsTrue(); await Assert.That(await File.ReadAllTextAsync(source)).Contains("void Run()");
			Result rename = await CliAsync(root, ipc, "rename", "Fixture.Widget.Run", "Execute");
			await Assert.That(rename.Code == 0).IsTrue();
			string renamed = await File.ReadAllTextAsync(source); await Assert.That(renamed).Contains("void Execute()"); await Assert.That(renamed).Contains(".Execute()");
			Result after = await CliAsync(root, ipc, "refs", "Fixture.Widget.Execute");
			await Assert.That(after.Code == 0).IsTrue(); await Assert.That(after.Output).Contains("Widget.cs");
			Result status = await CliAsync(root, ipc, "server", "status"); await Assert.That(status.Output).Contains(pid);
			Result missing = await CliAsync(root, ipc, "symbol", "Fixture.Missing");
			await Assert.That(missing.Code).IsEqualTo(1); await Assert.That(missing.Output).IsEmpty(); await Assert.That(missing.Error).Contains("NotFound");
			Result invalid = await CliAsync(root, ipc, "rename", "Fixture.Widget"); await Assert.That(invalid.Code).IsEqualTo(2); await Assert.That(invalid.Output).IsEmpty();
			int daemonId = int.Parse(pid[4..], System.Globalization.CultureInfo.InvariantCulture);
			using (Process crashed = Process.GetProcessById(daemonId))
			{
				crashed.Kill(entireProcessTree: true);
				await crashed.WaitForExitAsync();
			}
			Result restarted = await CliAsync(root, ipc, "symbol", "Fixture.Widget");
			await Assert.That(restarted.Code == 0).IsTrue();
			Result freshStatus = await CliAsync(root, ipc, "server", "status");
			await Assert.That(freshStatus.Output).DoesNotContain(pid + " ");
			if (!OperatingSystem.IsWindows())
			{
				int freshId = int.Parse(freshStatus.Output.Split(' ').Single(part => part.StartsWith("pid="))[4..], System.Globalization.CultureInfo.InvariantCulture);
				using Process stopping = Process.GetProcessById(freshId);
				await Assert.That((await ProcessAsync(root, ipc, "kill", ["-TERM", freshId.ToString(System.Globalization.CultureInfo.InvariantCulture)])).Code).IsEqualTo(0);
				await stopping.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
				await Assert.That(File.Exists(new LocalEndpoint(ipc).SocketPath)).IsFalse();
			}


		}
		finally
		{
			await CliAsync(root, ipc, "server", "stop");
			var endpoint = new LocalEndpoint(ipc);
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			while (File.Exists(endpoint.SocketPath)) await Task.Delay(20, timeout.Token);
			Directory.Delete(root, true);
		}
	}

	private static Task<Result> CliAsync(string workingDirectory, string ipc, params string[] args) => ProcessAsync(workingDirectory, ipc, "dotnet", [typeof(CliApplication).Assembly.Location, .. args]);
	private static async Task<Result> ProcessAsync(string workingDirectory, string ipc, string executable, string[] args)
	{
		var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		foreach (string arg in args) start.ArgumentList.Add(arg);
		start.Environment["ROSLYNK_ENDPOINT_DIRECTORY"] = ipc;
		using Process process = Process.Start(start)!;
		Task<string> output = process.StandardOutput.ReadToEndAsync(); Task<string> error = process.StandardError.ReadToEndAsync();
		try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
		catch { process.Kill(entireProcessTree: true); throw; }
		return new(process.ExitCode, await output, await error);
	}
	private sealed record Result(int Code, string Output, string Error);
}