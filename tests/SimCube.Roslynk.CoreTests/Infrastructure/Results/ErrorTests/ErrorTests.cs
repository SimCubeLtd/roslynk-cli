using TUnit.Assertions.Enums;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Results.ErrorTests;

public class ErrorTests
{
	[Test]
	public async Task WhenNotFoundWithCandidates_ThenTheCodeAndCandidatesAreCarried()
	{
		Error subject = Error.NotFound("nothing matched", new[] { "A", "B" });

		await Assert.That(subject.Code).IsEqualTo(ErrorCode.NotFound);
		await Assert.That(subject.Candidates).IsEquivalentTo(new[] { "A", "B" }, CollectionOrdering.Matching);
		await Assert.That(subject.StaleFiles).IsNull();
	}

	[Test]
	public async Task WhenStale_ThenTheCodeAndStaleFilesAreCarried()
	{
		Error subject = Error.Stale("changed on disk", new[] { "Widget.cs" });

		await Assert.That(subject.Code).IsEqualTo(ErrorCode.Stale);
		await Assert.That(subject.StaleFiles).IsEquivalentTo(new[] { "Widget.cs" }, CollectionOrdering.Matching);
		await Assert.That(subject.Candidates).IsNull();
	}

	[Test]
	public async Task WhenIndexing_ThenTheCodeIsIndexing()
	{
		Error subject = Error.Indexing();

		await Assert.That(subject.Code).IsEqualTo(ErrorCode.Indexing);
		await Assert.That(string.IsNullOrWhiteSpace(subject.Message)).IsFalse();
	}

	[Test]
	public async Task WhenNotSupported_ThenTheCodeIsNotSupported()
	{
		Error subject = Error.NotSupported("not a C# file");

		await Assert.That(subject.Code).IsEqualTo(ErrorCode.NotSupported);
		await Assert.That(subject.Message).IsEqualTo("not a C# file");
	}
}
