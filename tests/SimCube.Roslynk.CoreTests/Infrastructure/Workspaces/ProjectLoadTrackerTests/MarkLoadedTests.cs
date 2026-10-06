using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Workspaces.ProjectLoadTrackerTests;

public class MarkLoadedTests
{
	[Test]
	public async Task WhenTheSameProjectIsReportedTwice_ThenItCountsOnce()
	{
		var subject = new ProjectLoadTracker();

		subject.MarkLoaded(@"C:\app\Lib.csproj");
		subject.MarkLoaded(@"C:\app\Lib.csproj");

		await Assert.That(subject.Count).IsEqualTo(1);
	}

	[Test]
	public async Task WhenDifferentProjectsAreReported_ThenEachCounts()
	{
		var subject = new ProjectLoadTracker();

		subject.MarkLoaded(@"C:\app\One.csproj");
		subject.MarkLoaded(@"C:\app\Two.csproj");

		await Assert.That(subject.Count).IsEqualTo(2);
	}

	[Test]
	public async Task WhenFilePathIsNull_ThenItThrows()
	{
		var subject = new ProjectLoadTracker();

		await Assert.That(() => subject.MarkLoaded(null!)).ThrowsExactly<ArgumentNullException>();
	}
}