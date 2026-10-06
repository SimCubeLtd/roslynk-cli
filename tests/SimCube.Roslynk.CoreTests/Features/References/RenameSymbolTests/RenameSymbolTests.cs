using System.IO;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.References.RenameSymbolTests;

public class RenameSymbolTests
{
	[Test]
	public async Task WhenRenamingAType_ThenItsDeclarationAndReferencesAreRewrittenOnDisk()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "SimpleLibrary");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(solutionPath, "SimpleLibrary.Greeter", "Welcomer");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Greeter");
		await Assert.That(result.Split('\n')).Contains(line => line == "SimpleLibrary");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Greeter.cs");

		string greeter = await File.ReadAllTextAsync(Path.Combine(libraryDir, "Greeter.cs"));
		await Assert.That(greeter).Contains("class Welcomer");

		string caller = await File.ReadAllTextAsync(Path.Combine(libraryDir, "Caller.cs"));
		await Assert.That(caller).Contains("new Welcomer()");
	}

	[Test]
	public async Task WhenTheNewNameIsNotAValidIdentifier_ThenItIsRefusedAndNothingIsWritten()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(TestSolutions.Simple, "SimpleLibrary.Greeter", "1nvalid");

		await Assert.That(result).Contains("error=Invalid");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(TestSolutions.Simple, "SimpleLibrary.Greeter", "Welcomer");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenOnlyThatOverloadIsRenamed()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "SimpleLibrary");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(solutionPath, "SimpleLibrary.Ledger.Add(int, int)", "AddRepeatedly");

		await Assert.That(result).Contains("applied=Y");
		// The echoed name is the one that was resolved, i.e. before the rename.
		await Assert.That(result).Contains("resolvedSymbol=SimpleLibrary.Ledger.Add(int, int)");

		string ledger = await File.ReadAllTextAsync(Path.Combine(libraryDir, "Ledger.cs"));
		await Assert.That(ledger).Contains("public int AddRepeatedly(int amount, int times)");
		await Assert.That(ledger).Contains("public int Add(int amount)");
	}

	[Test]
	public async Task WhenTheNameMatchesSeveralOverloads_ThenAmbiguousCandidatesAreDistinguishable()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(TestSolutions.Simple, "SimpleLibrary.Ledger.Add", "Accumulate");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int)\n");
		await Assert.That(result).Contains("candidate=SimpleLibrary.Ledger.Add(int, int)\n");
	}

}