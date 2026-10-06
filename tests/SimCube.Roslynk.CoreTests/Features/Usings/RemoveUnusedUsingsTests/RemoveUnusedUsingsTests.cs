using System.IO;
using SimCube.Roslynk.Core.Features.Usings.RemoveUnusedUsings;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Writing;
using SimCube.Roslynk.CoreTests.Helpers;

namespace SimCube.Roslynk.CoreTests.Features.Usings.RemoveUnusedUsingsTests;

public class RemoveUnusedUsingsTests
{
	[Test]
	public async Task WhenAUsingHasACommentAboveIt_ThenTheCommentSurvives()
	{
		// The reason this tool now goes through Roslyn's own fix: removing the node took the comment with it.
		string solutionPath = UnnecessaryUsingScenario.Create(out string greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.RemoveUnusedUsings(registry);

		string result = await subject.RemoveUnusedUsings(solutionPath);

		await Assert.That(result).Contains("applied=Y");
		string content = await File.ReadAllTextAsync(greeter);
		await Assert.That(content).DoesNotContain(UnnecessaryUsingScenario.UnnecessaryUsing);
		await Assert.That(content).Contains(UnnecessaryUsingScenario.Comment);
	}

	[Test]
	public async Task WhenAFileHasAnUnnecessaryUsing_ThenItIsRemoved()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string greeter = FindFile(solutionPath, "Greeter.cs");
		await File.WriteAllTextAsync(greeter, "using System.Text;\r\n" + await File.ReadAllTextAsync(greeter));

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.RemoveUnusedUsings(registry);

		string result = await subject.RemoveUnusedUsings(solutionPath);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).DoesNotContain("removedCount=0");
		await Assert.That(await File.ReadAllTextAsync(greeter)).DoesNotContain("using System.Text;");
	}

	[Test]
	public async Task WhenCheckOnly_ThenTheUsingIsReportedButNotRemoved()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string greeter = FindFile(solutionPath, "Greeter.cs");
		string withUnused = "using System.Text;\r\n" + await File.ReadAllTextAsync(greeter);
		await File.WriteAllTextAsync(greeter, withUnused);

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.RemoveUnusedUsings(registry);

		string result = await subject.RemoveUnusedUsings(solutionPath, documentPath: null, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result).Contains("Greeter.cs");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(withUnused);
	}

	[Test]
	public async Task WhenThereAreNoUnnecessaryUsings_ThenNothingIsApplied()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = TestServices.RemoveUnusedUsings(registry);

		string result = await subject.RemoveUnusedUsings(TestSolutions.Simple);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result).Contains("removedCount=0");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = TestServices.RemoveUnusedUsings(registry);

		string result = await subject.RemoveUnusedUsings(TestSolutions.Simple);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static string FindFile(string solutionPath, string fileName) =>
		Directory.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, fileName, SearchOption.AllDirectories).First();
}