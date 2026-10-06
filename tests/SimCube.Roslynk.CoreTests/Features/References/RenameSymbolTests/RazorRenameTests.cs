using System.IO;
using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.References.RenameSymbolTests;

/// <summary>
/// Renaming symbols declared in .razor files: the Renamer's edits land in the Razor-generated .g.cs
/// documents, are mapped back through the #line directives, and are written to the .razor sources.
/// </summary>
public class RazorRenameTests
{
	private static RenameSymbolTool CreateSubject(InstanceRegistry registry) =>
		new(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

	[Test]
	public async Task WhenRenamingAFieldDeclaredInARazorCodeBlock_ThenTheRazorFileIsRewrittenOnDisk()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string counterPath = Path.Combine(Path.GetDirectoryName(solutionPath)!, "RazorLib", "Counter.razor");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		RenameSymbolTool subject = CreateSubject(registry);

		string result = await subject.RenameSymbol(solutionPath, "RazorLib.Counter.CurrentCount", "Total");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("resolvedSymbol=RazorLib.Counter.CurrentCount");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");

		string counter = await File.ReadAllTextAsync(counterPath);
		await Assert.That(counter).Contains("private int Total;");
		await Assert.That(counter).Contains("Count: @Total");
		await Assert.That(counter).Contains("Total = StartAt;");
		await Assert.That(counter).Contains("Total++;");
		await Assert.That(counter).DoesNotContain("CurrentCount");
	}

	[Test]
	public async Task WhenRenamingAMethodWiredOnlyInMarkup_ThenTheMarkupAttributeIsRewritten()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string counterPath = Path.Combine(Path.GetDirectoryName(solutionPath)!, "RazorLib", "Counter.razor");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		RenameSymbolTool subject = CreateSubject(registry);

		string result = await subject.RenameSymbol(solutionPath, "RazorLib.Counter.IncrementCount", "Bump");

		await Assert.That(result).Contains("applied=Y");

		string counter = await File.ReadAllTextAsync(counterPath);
		await Assert.That(counter).Contains("@onclick=\"Bump\"");
		await Assert.That(counter).Contains("private void Bump()");
		await Assert.That(counter).DoesNotContain("IncrementCount");
	}

	[Test]
	public async Task WhenCheckOnlyIsPassed_ThenTheRazorFileIsListedAndNothingIsWritten()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string counterPath = Path.Combine(Path.GetDirectoryName(solutionPath)!, "RazorLib", "Counter.razor");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		RenameSymbolTool subject = CreateSubject(registry);

		string result = await subject.RenameSymbol(solutionPath, "RazorLib.Counter.CurrentCount", "Total", checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");

		string counter = await File.ReadAllTextAsync(counterPath);
		await Assert.That(counter).Contains("CurrentCount");
		await Assert.That(counter).DoesNotContain("Total");
	}

	[Test]
	public async Task WhenRenamingAComponentParameter_ThenAttributeUsagesInOtherComponentsAreRewritten()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "RazorLib");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		RenameSymbolTool subject = CreateSubject(registry);

		string result = await subject.RenameSymbol(solutionPath, "RazorLib.Counter.StartAt", "StartFrom");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "UsesCounter.razor");

		string counter = await File.ReadAllTextAsync(Path.Combine(libraryDir, "Counter.razor"));
		await Assert.That(counter).Contains("public int StartFrom { get; set; }");
		await Assert.That(counter).Contains("Starting from @StartFrom");
		await Assert.That(counter).DoesNotContain("StartAt");

		// The generator emits the attribute name as nameof(...) inside a #line-mapped region, so the
		// markup usage in the consuming component is renamed too.
		string usesCounter = await File.ReadAllTextAsync(Path.Combine(libraryDir, "UsesCounter.razor"));
		await Assert.That(usesCounter).Contains("<Counter StartFrom=\"5\" />");
	}

	[Test]
	public async Task WhenARazorRenameIsApplied_ThenSubsequentReadsSeeTheNewName()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		RenameSymbolTool rename = CreateSubject(registry);

		string renameResult = await rename.RenameSymbol(solutionPath, "RazorLib.Counter.CurrentCount", "Total");
		await Assert.That(renameResult).Contains("applied=Y");

		var findReferences = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());
		string referencesResult = await findReferences.FindReferences(solutionPath, "RazorLib.Counter.Total");

		await Assert.That(referencesResult).Contains("resolvedSymbol=RazorLib.Counter.Total");
		await Assert.That(referencesResult.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");
	}
}