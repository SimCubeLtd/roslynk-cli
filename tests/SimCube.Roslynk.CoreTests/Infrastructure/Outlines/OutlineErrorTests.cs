using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Outlines;

public class OutlineErrorTests
{
	[Test]
	public async Task WhenAmbiguous_ThenEachCandidateIsItsOwnRepeatableHeader()
	{
		string result = OutlineError.Format(
			Error.Ambiguous("'X' matched several symbols.", ["N.A", "N.B"]),
			SolutionStatus.Ready);

		await Assert.That(result).IsEqualTo("error=Ambiguous\nerrorMessage='X' matched several symbols.\ncandidate=N.A\ncandidate=N.B\n");
	}

	[Test]
	public async Task WhenStale_ThenEachStaleFileIsItsOwnRepeatableHeader()
	{
		string result = OutlineError.Format(
			Error.Stale("Files moved on disk.", ["src/A.cs", "src/B.cs"]),
			SolutionStatus.Ready);

		await Assert.That(result).IsEqualTo("error=Stale\nerrorMessage=Files moved on disk.\nstale=src/A.cs\nstale=src/B.cs\n");
	}

	[Test]
	public async Task WhenTheMessageContainsNewlines_ThenTheyAreCollapsedToKeepOneLine()
	{
		string result = OutlineError.Format(
			Error.Invalid("first\r\nsecond"),
			SolutionStatus.Building);

		await Assert.That(result).DoesNotContain("\r");
		await Assert.That(result).Contains("errorMessage=first  second\n");
	}
}