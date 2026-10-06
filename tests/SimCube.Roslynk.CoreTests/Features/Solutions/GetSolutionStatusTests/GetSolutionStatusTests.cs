using SimCube.Roslynk.Core.Features.Solutions.GetSolutionStatus;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.Solutions.GetSolutionStatusTests;

public class GetSolutionStatusTests
{
	[Test]
	public async Task WhenASolutionIsLoaded_ThenItIsReadyWithLoadedMatchingTotal()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetSolutionStatusTool(registry);

		string result = subject.GetSolutionStatus();

		// One line "<solutionId>,Ready,1/1".
		string line = result.Split('\n').First(candidate => candidate.Contains(",Ready,", StringComparison.Ordinal));
		await Assert.That(line).EndsWith(",1/1");
	}

	[Test]
	public async Task WhileASolutionIsStillLoading_ThenTheTotalIsUnknownAndStatusIsBuilding()
	{
		using var registry = new InstanceRegistry();
		registry.GetOrBegin(TestSolutions.Simple);
		var subject = new GetSolutionStatusTool(registry);

		string result = subject.GetSolutionStatus();

		await Assert.That(result).Contains(",Building,");
		await Assert.That(result).Contains("/?");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
}