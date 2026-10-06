using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.InstanceRegistryTests;

public class GetOrAddAsyncTests
{
	[Test]
	public async Task WhenTheSameSolutionIsRequestedTwice_ThenTheSameInstanceIsShared()
	{
		using var subject = new InstanceRegistry();

		RoslynInstance first = await subject.GetOrAddAsync(TestSolutions.Simple);
		RoslynInstance second = await subject.GetOrAddAsync(TestSolutions.Simple);

		await Assert.That(second).IsSameReferenceAs(first);
	}
}