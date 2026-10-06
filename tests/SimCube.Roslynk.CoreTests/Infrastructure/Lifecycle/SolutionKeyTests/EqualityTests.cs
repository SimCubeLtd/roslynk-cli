using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.SolutionKeyTests;

public class EqualityTests
{
	[Test]
	public async Task WhenPathsDifferOnlyInCase_ThenEqualityFollowsThePlatformCasePolicy()
	{
		string firstPath = OperatingSystem.IsWindows() ? @"C:\Solutions\App\App.slnx" : "/Solutions/App/App.slnx";
		string secondPath = OperatingSystem.IsWindows() ? @"C:\solutions\app\APP.SLNX" : "/solutions/app/APP.SLNX";

		SolutionKey first = SolutionKey.For(firstPath);
		SolutionKey second = SolutionKey.For(secondPath);

		bool expectEqual = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

		await Assert.That(first.Equals(second)).IsEqualTo(expectEqual);
		if (expectEqual)
			await Assert.That(second.GetHashCode()).IsEqualTo(first.GetHashCode());
	}

	[Test]
	public async Task WhenGivenARelativePath_ThenTheKeyIsMadeAbsolute()
	{
		SolutionKey subject = SolutionKey.For("App.slnx");

		await Assert.That(System.IO.Path.IsPathFullyQualified(subject.FilePath)).IsTrue();
	}
}