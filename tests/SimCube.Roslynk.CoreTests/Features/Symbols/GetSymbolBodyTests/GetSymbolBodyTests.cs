using SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.GetSymbolBodyTests;

public class GetSymbolBodyTests
{
	[Test]
	public async Task WhenAMethodIsRequested_ThenTheWholeDeclarationIncludingItsBodyIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Calculator.Add");

		await Assert.That(result).Contains("project=SimpleLibrary\n");
		await Assert.That(result).Contains("path=SimpleLibrary/Calculator.cs\n");
		await Assert.That(result).Contains("loc=");
		await Assert.That(result).Contains("public int Add(int a, int b)");
		await Assert.That(result).Contains("return a + b;");
		await Assert.That(result).DoesNotContain("error=");
	}

	[Test]
	public async Task WhenTheDeclarationIsIndented_ThenTheSourceTextIsPreservedExactly()
	{
		string result = await RunAsync("SimpleLibrary.Calculator.Add");

		// Original tabs and line breaks survive: the body is the file's text, not a reformatted rendering.
		await Assert.That(Normalize(result)).Contains("\t{\n\t\treturn a + b;\n\t}");
	}

	[Test]
	public async Task WhenLeadingTriviaIsNotRequested_ThenTheTextStartsAtTheDeclaration()
	{
		string result = await RunAsync("SimpleLibrary.Widget.Compute");

		await Assert.That(Body(result)).StartsWith("public int Compute(int value)");
		await Assert.That(result).DoesNotContain("<summary>");
	}

	[Test]
	public async Task WhenLeadingTriviaIsRequested_ThenTheDocumentationCommentIsIncluded()
	{
		string result = await RunAsync("SimpleLibrary.Widget.Compute", includeLeadingTrivia: true);

		await Assert.That(Body(result)).StartsWith("/// <summary>Doubles");
		await Assert.That(result).Contains("public int Compute(int value) => value * 2;");
	}

	[Test]
	public async Task WhenLeadingTriviaIsRequested_ThenTheLocStartsAtTheTriviaNotTheDeclaration()
	{
		string withoutTrivia = await RunAsync("SimpleLibrary.Widget.Compute");
		string withTrivia = await RunAsync("SimpleLibrary.Widget.Compute", includeLeadingTrivia: true);

		await Assert.That(Header(withTrivia, "loc")).IsNotEqualTo(Header(withoutTrivia, "loc"));
	}

	[Test]
	public async Task WhenATypeIsRequested_ThenTheWholeTypeDeclarationIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Greeter");

		await Assert.That(result).Contains("public class Greeter : IGreeter");
		await Assert.That(result).Contains("public string Greet(string name) => $\"Hello, {name}!\";");
	}

	[Test]
	public async Task WhenAFieldIsRequested_ThenTheWholeFieldDeclarationIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Holder._ready");

		await Assert.That(result).Contains("private bool _ready;");
	}

	[Test]
	public async Task WhenAPropertyIsRequested_ThenItsAccessorListIsReturnedVerbatim()
	{
		string result = await RunAsync("SimpleLibrary.Ledger.Total");

		await Assert.That(result).Contains("public int Total { get; private set; }");
	}

	[Test]
	public async Task WhenASymbolIsDeclaredInSeveralParts_ThenEveryPartIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Ledger");

		await Assert.That(result).Contains("parts=2\n");
		await Assert.That(result).Contains("part=1,project=SimpleLibrary,path=SimpleLibrary/Ledger.cs,loc=");
		await Assert.That(result).Contains("part=2,project=SimpleLibrary,path=SimpleLibrary/Ledger.Totals.cs,loc=");
		await Assert.That(result).Contains("Total += amount;");
		await Assert.That(result).Contains("public int Total { get; private set; }");
	}

	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenAmbiguousIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Ledger.Add");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add");
	}

	[Test]
	public async Task WhenTheSymbolIsMetadataOnly_ThenNotSupportedIsReturned()
	{
		string result = await RunAsync("System.String");

		await Assert.That(result).Contains("error=NotSupported");
		await Assert.That(result).DoesNotContain("path=");
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
		var subject = new GetSymbolBodyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbolBody(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static string Normalize(string result) => result.Replace("\r\n", "\n");

	private static string Body(string result)
	{
		string normalized = Normalize(result);
		int separator = normalized.IndexOf("\n\n", StringComparison.Ordinal);
		return separator < 0 ? "" : normalized[(separator + 2)..];
	}

	private static string Header(string result, string key)
	{
		foreach (string line in Normalize(result).Split('\n'))
		{
			if (line.StartsWith(key + "=", StringComparison.Ordinal))
				return line;
		}

		return "";
	}

	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenOnlyThatOverloadIsReturned()
	{
		string result = await RunAsync("SimpleLibrary.Ledger.Add(int, int)");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("path=SimpleLibrary/Ledger.cs\n");
		await Assert.That(result).Contains("public int Add(int amount, int times)");
		await Assert.That(result).DoesNotContain("Adds <paramref");
	}

	private static async Task<string> RunAsync(string symbolName, bool includeLeadingTrivia = false)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetSymbolBodyTool(registry, new SymbolResolver(), new ProjectionService());

		return await subject.GetSymbolBody(TestSolutions.Simple, symbolName, includeLeadingTrivia);
	}
}