using System.IO;
using SimCube.Roslynk.Cli;
using SimCube.Roslynk.Protocol;

namespace SimCube.Roslynk.TransportTests;

public sealed class CliTests
{
	[Test]
	[Arguments("--help")]
	[Arguments("refs --help")]
	[Arguments("server --help")]
	[Arguments("patch --help")]
	[Arguments("batch --help")]
	public async Task WhenHelpIsRequested_ThenItIsDiscoverableWithoutStartingADaemon(string command)
	{
		var output = new StringWriter(); var error = new StringWriter();
		var cli = new CliApplication(new LocalEndpoint(Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"))), output, error);
		await Assert.That(await cli.RunAsync(command.Split(' '))).IsEqualTo(0);
		await Assert.That(output.ToString()).Contains("Usage:");
		await Assert.That(output.ToString()).Contains("roslynk-cli");
		await Assert.That(error.ToString()).IsEqualTo("");
	}

	[Test]
	[Arguments("refs")]
	[Arguments("rename N.T")]
	[Arguments("refs N.T --unknown")]
	[Arguments("definition File.cs nope 1")]
	public async Task WhenArgumentsAreInvalid_ThenErrorsUseStderrAndExitTwo(string command)
	{
		var output = new StringWriter(); var error = new StringWriter();
		await Assert.That(await new CliApplication(output: output, error: error).RunAsync(command.Split(' '))).IsEqualTo(2);
		await Assert.That(output.ToString()).IsEqualTo(""); await Assert.That(error.ToString()).IsNotEmpty();
	}

	[Test]
	[Arguments("src")]
	[Arguments("Source")]
	public async Task WhenSolutionIsUnique_ThenDiscoveryWalksUpAndSupportsSourceLayout(string sourceDirectory)
	{
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(root, sourceDirectory, "Nested"));
			string solution = Path.Combine(root, sourceDirectory, "Fixture.slnx"); File.WriteAllText(solution, "");
			await Assert.That(SolutionDiscovery.Find(null, Path.Combine(root, sourceDirectory, "Nested"))).IsEqualTo(solution);
			await Assert.That(SolutionDiscovery.Find(null, root)).IsEqualTo(solution);
			File.WriteAllText(Path.Combine(root, sourceDirectory, "Other.sln"), "");
			await Assert.That((await Assert.That(() => SolutionDiscovery.Find(null, root)).ThrowsExactly<ArgumentException>())!.Message).Contains("--solution");
			await Assert.That(SolutionDiscovery.Find(Path.Combine(sourceDirectory, "Fixture.slnx"), root)).IsEqualTo(solution);
		}
		finally { Directory.Delete(root, true); }
	}

	[Test]
	public async Task WhenTheDaemonIsUnavailable_ThenExplicitStatusDoesNotLaunchIt()
	{
		string root = Path.Combine(Path.GetTempPath(), "rk-" + Guid.NewGuid().ToString("N"));
		try
		{
			var endpoint = new LocalEndpoint(root);
			var output = new StringWriter(); var error = new StringWriter();
			await Assert.That(await new CliApplication(endpoint, output, error).RunAsync(["server", "status"])).IsEqualTo(1);
			await Assert.That(error.ToString()).IsEqualTo("Roslynk daemon is not running." + Environment.NewLine); await Assert.That(output.ToString()).IsEqualTo("");
			await Assert.That(File.Exists(endpoint.SocketPath)).IsFalse();
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}
}