using SimCube.Roslynk.Core.Features.Symbols.GetSymbol;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.GetSymbolTests;

public class GetSymbolTests
{
	[Test]
	public async Task WhenATypeIsRequested_ThenLeanReturnsPathLocAndTheDeclaratorThroughItsBaseList()
	{
		string result = await RunAsync("SimpleLibrary.Greeter");

		await Assert.That(result).Contains("project=SimpleLibrary\n");
		await Assert.That(result).Contains("path=SimpleLibrary/Greeter.cs");
		await Assert.That(result).Contains("loc=");
		await Assert.That(result).Contains("public class Greeter : IGreeter");
		await Assert.That(result).DoesNotContain("status=");
		// A source symbol omits #source (it is the implied common case).
		await Assert.That(result).DoesNotContain("source=");
	}

	[Test]
	public async Task WhenAnExpressionBodiedMethodIsRequested_ThenTheBodyIsCutAtTheArrow()
	{
		string result = await RunAsync("SimpleLibrary.Widget.Compute");

		await Assert.That(result).Contains("public int Compute(int value)");
		await Assert.That(result).DoesNotContain("value * 2");
	}

	[Test]
	public async Task WhenAMultiLineSignatureMethodIsRequested_ThenItIsKeptThroughTheClosingParenButNotTheBody()
	{
		string result = await RunAsync("SimpleLibrary.Holder.Combine");

		await Assert.That(result).Contains("public string Combine(");
		await Assert.That(result).Contains("string second)");
		await Assert.That(result).DoesNotContain("_ready");
	}

	[Test]
	public async Task WhenAnAutoPropertyIsRequested_ThenTheDeclaratorKeepsItsAccessorList()
	{
		string result = await RunAsync("SimpleLibrary.Holder.Count");

		await Assert.That(result).Contains("public int Count { get; set; }");
	}

	[Test]
	public async Task WhenAFieldIsRequested_ThenTheDeclaratorShowsModifiersTypeAndName()
	{
		string result = await RunAsync("SimpleLibrary.Holder._ready");

		await Assert.That(result).Contains("private bool _ready");
	}

	[Test]
	public async Task WhenAMetadataSymbolIsRequested_ThenSourceKindSignatureAndAssemblyAreReturnedWithoutAPath()
	{
		string result = await RunAsync("System.String");

		await Assert.That(result).Contains("source=metadata");
		await Assert.That(result).Contains("kind=class");
		await Assert.That(result).Contains("signature=");
		await Assert.That(result).Contains("assembly=");
		await Assert.That(result).DoesNotContain("path=");
		await Assert.That(result).DoesNotContain("project=");
	}

	[Test]
	public async Task WhenTheSymbolDoesNotExist_ThenNotFoundIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.DoesNotExist");

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheNameNearlyMatches_ThenRankedCandidatesAreSuggested()
	{
		string result = await RunAsync("SimpleLibrary.Greet");

		await Assert.That(result).Contains("error=NotFound");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Greeter");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetSymbolTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbol(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenAmbiguousIsReturnedWithDistinguishableCandidates()
	{
		string result = await RunAsync("SimpleLibrary.Ledger.Add");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int)\n");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int, int)\n");
	}

	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenItsDeclarationIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Ledger.Add(int, int)");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("public int Add(int amount, int times)");
	}

	private static async Task<string> RunAsync(string symbolName)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetSymbolTool(registry, new SymbolResolver(), new ProjectionService());

		return await subject.GetSymbol(TestSolutions.Simple, symbolName);
	}
}