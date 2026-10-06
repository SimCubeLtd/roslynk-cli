using SimCube.Roslynk.Core.Features.Symbols.FindImplementations;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.FindImplementationsTests;

public class FindImplementationsTests
{
	[Test]
	public async Task WhenAnInterfaceIsRequested_ThenItsImplementorsAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindImplementationsTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindImplementations(TestSolutions.Simple, "SimpleLibrary.IGreeter");

		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.IGreeter");
		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("\tSimpleLibrary\n");
		await Assert.That(result).Contains("class,Greeter,");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new FindImplementationsTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindImplementations(TestSolutions.Simple, "SimpleLibrary.IGreeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenAmbiguousIsReturnedWithDistinguishableCandidates()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindImplementationsTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindImplementations(TestSolutions.Simple, "SimpleLibrary.Ledger.Add");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int)\n");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int, int)\n");
	}

}