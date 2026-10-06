using SimCube.Roslynk.Core.Features.Callers.GetCallers;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Callers.GetCallersTests;

public class GetCallersTests
{
	[Test]
	public async Task WhenAMethodIsCalled_ThenItsCallersAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Simple, "SimpleLibrary.Greeter.Greet");

		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Greeter.Greet");
		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("class,Caller\n");
		await Assert.That(result).Contains("method,Run,");
	}

	[Test]
	public async Task WhenTheMethodIsNotFound_ThenNotFoundIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Simple, "SimpleLibrary.DoesNotExist");

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Simple, "SimpleLibrary.Greeter.Greet");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenOnlyThatOverloadsCallersAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Simple, "SimpleLibrary.Ledger.Add(int)");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Ledger.Add(int)");
		// Add(int) is called by Add(int, int); the reverse is not true.
		await Assert.That(result).Contains("method,Add,");
	}

	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenAmbiguousCandidatesAreDistinguishable()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Simple, "SimpleLibrary.Ledger.Add");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int)\n");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int, int)\n");
	}

}