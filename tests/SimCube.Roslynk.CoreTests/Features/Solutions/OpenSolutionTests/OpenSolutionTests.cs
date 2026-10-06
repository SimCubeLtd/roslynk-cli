using SimCube.Roslynk.Core.Features.Solutions.OpenSolution;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.Solutions.OpenSolutionTests;

public class OpenSolutionTests
{
	[Test]
	public async Task WhenOpeningASolution_ThenItReturnsImmediatelyAndBuildsInTheBackground()
	{
		using var registry = new InstanceRegistry();
		var subject = new OpenSolutionTool(registry);

		string opening = subject.OpenSolution(TestSolutions.Simple);

		await Assert.That(opening).Contains("status=Building");
		await Assert.That(opening).Contains("projects=0");

		await registry.GetOrAddAsync(TestSolutions.Simple);
		string ready = subject.OpenSolution(TestSolutions.Simple);

		await Assert.That(ready).DoesNotContain("status");
		await Assert.That(ready).Contains("projects=1");
		await Assert.That(ready).Contains("SimpleLibrary.csproj,");
	}

	[Test]
	public async Task WhenOpeningASolutionThatDoesNotExist_ThenItReturnsNotFoundImmediately()
	{
		using var registry = new InstanceRegistry();
		var subject = new OpenSolutionTool(registry);

		string nonexistentPath = Path.Combine(Path.GetTempPath(), "DoesNotExist", "Solution.slnx");
		string result = subject.OpenSolution(nonexistentPath);

		await Assert.That(result).Contains("error=NotFound");
		await Assert.That(result).Contains("No solution file was found at");
		await Assert.That(registry.OpenSolutionPaths).IsEmpty();
	}
}