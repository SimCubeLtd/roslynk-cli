using SimCube.Roslynk.Core.Features.Solutions.ReloadSolution;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.Solutions.ReloadSolutionTests;

public class ReloadSolutionTests
{
	[Test]
	public async Task WhenReloadingASolution_ThenThePreviousSnapshotIsServedWhileItRebuilds()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ReloadSolutionTool(registry);

		string result = subject.ReloadSolution(TestSolutions.Simple);

		await Assert.That(result).Contains("status=Building");
		await Assert.That(result).DoesNotContain("projects=0");

		await WaitForReadyAsync(registry, TestSolutions.Simple);
	}

	private static async Task WaitForReadyAsync(InstanceRegistry registry, string solutionPath)
	{
		RoslynInstance instance = registry.GetOrBegin(solutionPath);
		DateTime deadline = DateTime.UtcNow.AddSeconds(60);
		while (instance.CurrentModel.Status != SolutionStatus.Ready)
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("The reload did not complete in time.");
			await Task.Delay(25);
		}
	}
}