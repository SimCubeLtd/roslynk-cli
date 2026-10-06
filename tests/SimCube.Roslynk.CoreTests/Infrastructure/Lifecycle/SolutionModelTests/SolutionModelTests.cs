using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.SolutionModelTests;

public class SolutionModelTests
{
	[Test]
	public async Task WhenLoading_ThenStatusIsBuildingAndSolutionIsNull()
	{
		SolutionModel subject = SolutionModel.Loading(solution: null);

		await Assert.That(subject.Status).IsEqualTo(SolutionStatus.Building);
		await Assert.That(subject.Solution).IsNull();
	}

	[Test]
	public async Task WhenFaulted_ThenStatusIsFaultedAndTheMessageIsKept()
	{
		SolutionModel subject = SolutionModel.Faulted("load failed");

		await Assert.That(subject.Status).IsEqualTo(SolutionStatus.Faulted);
		await Assert.That(subject.FaultMessage).IsEqualTo("load failed");
		await Assert.That(subject.Solution).IsNull();
	}
}