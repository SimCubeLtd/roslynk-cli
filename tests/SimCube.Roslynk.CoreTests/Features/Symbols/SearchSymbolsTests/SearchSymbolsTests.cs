using SimCube.Roslynk.Core.Features.Symbols.SearchSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.SearchSymbolsTests;

public class SearchSymbolsTests
{
	[Test]
	public async Task WhenSearchingByNameSubstring_ThenMatchingSymbolsAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.Simple, "Greet");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("\tSimpleLibrary\n");
		await Assert.That(result).Contains("class,Greeter");
	}

	[Test]
	public async Task WhenNothingMatchesTheQuery_ThenNoResultsAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.Simple, "NoSuchSymbolNameHere");

		await Assert.That(result).IsEqualTo("");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.Simple, "Greet");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
	[Test]
	public async Task WhenAQueryMatchesSeveralOverloads_ThenEveryOverloadIsListed()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.Simple, "Add");

		await Assert.That(result).DoesNotContain("error=");
		// Both Ledger.Add overloads are listed; before the dedupe key carried the signature, one was dropped.
		await Assert.That(result.Split('\n').Count(line => line.TrimStart('\t').StartsWith("method,Add,", StringComparison.Ordinal))).IsEqualTo(2);
	}
}