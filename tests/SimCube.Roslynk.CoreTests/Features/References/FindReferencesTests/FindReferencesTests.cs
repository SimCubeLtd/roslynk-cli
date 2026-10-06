using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.References.FindReferencesTests;

public class FindReferencesTests
{
	[Test]
	public async Task WhenAReferencedTypeIsRequested_ThenTheOutlineNestsItByFileNamespaceTypeAndMember()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Greeter");
		await Assert.That(result).DoesNotContain("truncated");
		await Assert.That(result).DoesNotContain("count");
		await Assert.That(result.Split('\n')).Contains(line => line == "SimpleLibrary");
		await Assert.That(result.Split('\n')).Contains(line => line == "\tSimpleLibrary");
		await Assert.That(result.Split('\n')).Contains(line => line == "\t\tCaller.cs");
		await Assert.That(result).Contains("\t\t\t\tclass,Caller\n");
		await Assert.That(result).Contains("\t\t\t\t\tmethod,Run,5:");
		await Assert.That(result).DoesNotContain("\r");
	}

	[Test]
	public async Task WhenTheSymbolIsNotFound_ThenANotFoundHeaderIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.DoesNotExist");

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenMoreReferencesMatchThanMaxResults_ThenTheHeaderReportsTruncated()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Greeter", maxResults: 0);

		await Assert.That(result).Contains("truncated=Y");
		await Assert.That(result).Contains("count=");
		await Assert.That(result).DoesNotContain("count=0");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenAnIndexingHeaderIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	[Test]
	public async Task WhenManyReferencesShareDeclarations_ThenTheyNestUnderTheirContainingTypeAndMember()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.References);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.References, "RefSpace.IThing");

		await Assert.That(result).Contains("resolvedSymbol=RefSpace.IThing");
		await Assert.That(result).DoesNotContain("count");
		await Assert.That(result).DoesNotContain("truncated");
		await Assert.That(result).DoesNotContain("\r");

		await Assert.That(result.Split('\n')).Contains(line => line == "\t\tThings.cs");
		await Assert.That(result.Split('\n')).Contains(line => line == "\t\tOuter.cs");
		await Assert.That(result).Contains("\t\t\tRefSpace\n");

		// A type referenced in its own base list carries a location; Alpha/Beta sit at namespace depth.
		await Assert.That(result).Contains("\t\t\t\tclass,Alpha,");
		await Assert.That(result).Contains("\t\t\t\tclass,Beta,");

		// Outer is referenced nowhere itself, so it is a parent-only line; Nested nests one level deeper.
		await Assert.That(result).Contains("\t\t\t\tclass,Outer\n");
		await Assert.That(result).Contains("\t\t\t\t\tclass,Nested,");

		// Methods nest under their type; a method declaring two locals plus a return type lists three locations.
		await Assert.That(LocationCountUnder(result, "method,AlphaPair")).IsEqualTo(3);
		await Assert.That(LocationCountUnder(result, "method,NestedPair")).IsEqualTo(3);
	}

	private static int LocationCountUnder(string text, string declaration)
	{
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.TrimStart('\t');
			if (!trimmed.StartsWith(declaration + ",", StringComparison.Ordinal))
				continue;

			// trimmed is "kind,name,loc|loc|...": kind and name have no commas, so the third field is the list.
			string[] parts = trimmed.Split(',');
			return parts[2].Split('|').Length;
		}

		return -1;
	}
	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenOnlyThatOverloadsReferencesAreReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Ledger.Add(int)");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Ledger.Add(int)");
	}

	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenTheCandidatesAreAcceptedBackVerbatim()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string ambiguous = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Ledger.Add");
		await Assert.That(ambiguous).Contains("error=Ambiguous");
		await Assert.That(ambiguous).Contains("candidate=SimpleLibrary.Ledger.Add(int, int)\n");

		string resolved = await subject.FindReferences(TestSolutions.Simple, "SimpleLibrary.Ledger.Add(int, int)");
		await Assert.That(resolved).DoesNotContain("error=");
	}

}