using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.InstanceRegistryTests;

public class OpenSolutionPathsTests
{
	[Test]
	public async Task WhenNoSolutionsAreOpen_ThenItIsEmpty()
	{
		using var subject = new InstanceRegistry();

		await Assert.That(subject.OpenSolutionPaths).IsEmpty();
	}

	[Test]
	public async Task WhenASolutionIsOpen_ThenItsPathIsListed()
	{
		using var subject = new InstanceRegistry();
		await subject.GetOrAddAsync(TestSolutions.Simple);

		string path = await Assert.That(subject.OpenSolutionPaths).HasSingleItem();
		await Assert.That(path).Contains("SimpleSolution");
	}

	[Test]
	public async Task WhenAnOpenSolutionIsClosed_ThenItIsRemoved()
	{
		using var subject = new InstanceRegistry();
		await subject.GetOrAddAsync(TestSolutions.Simple);

		subject.TryClose(TestSolutions.Simple);

		await Assert.That(subject.OpenSolutionPaths).IsEmpty();
	}
}