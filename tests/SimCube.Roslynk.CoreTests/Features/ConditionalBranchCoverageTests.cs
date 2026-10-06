using SimCube.Roslynk.Core.Features.Callers.GetCallers;
using SimCube.Roslynk.Core.Features.Symbols.FindDefinition;
using SimCube.Roslynk.Core.Features.Symbols.FindImplementations;
using SimCube.Roslynk.Core.Features.Symbols.GetMembers;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbol;
using SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy;
using SimCube.Roslynk.Core.Features.Symbols.SearchSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features;

/// <summary>
/// Each multi-projection tool must surface symbols/usages declared in a branch (<c>#else</c>) that is inactive
/// in the loaded (DEBUG) configuration. The ConditionalSolution fixture pairs a DEBUG declaration with an
/// #else one for each tool; these assert the #else member appears.
/// </summary>
public class ConditionalBranchCoverageTests
{
	[Test]
	public async Task FindImplementations_FindsImplementorInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new FindImplementationsTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindImplementations(TestSolutions.Conditional, "ConditionalLib.IShape");

		await Assert.That(result).Contains("Circle");
	}

	[Test]
	public async Task GetTypeHierarchy_FindsDerivedTypeInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new GetTypeHierarchyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetTypeHierarchy(TestSolutions.Conditional, "ConditionalLib.Animal");

		await Assert.That(result).Contains("ConditionalLib.Cat");
	}

	[Test]
	public async Task GetMembers_FindsMemberInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new GetMembersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetMembers(TestSolutions.Conditional, "ConditionalLib.Box");

		await Assert.That(result).Contains("ReleaseOnly");
	}

	[Test]
	public async Task SearchSymbols_FindsTypeDeclaredOnlyInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.Conditional, "Widget");

		await Assert.That(result).Contains("ReleaseWidget");
	}

	[Test]
	public async Task GetCallers_FindsCallerInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.Conditional, "ConditionalLib.Target.Ping");

		await Assert.That(result).Contains("ReleaseCall");
	}

	[Test]
	public async Task GetSymbol_ResolvesSymbolDeclaredOnlyInElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new GetSymbolTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbol(TestSolutions.Conditional, "ConditionalLib.ReleaseWidget");

		await Assert.That(result).DoesNotContain("error=NotFound");
		await Assert.That(result).Contains("ReleaseWidget");
	}

	[Test]
	public async Task FindDefinition_ResolvesAUsageInsideTheElseBranch()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new FindDefinitionTool(registry, new SymbolResolver(), new ProjectionService());

		string navigation = Path.Combine(Path.GetDirectoryName(TestSolutions.Conditional)!, "ConditionalLib", "Navigation.cs");

		// Line 10 is the #else 'return new Target();'; column 15 lands inside 'Target'.
		string result = await subject.FindDefinition(TestSolutions.Conditional, navigation, 10, 15);

		await Assert.That(result).Contains("fullName=ConditionalLib.Target");
	}
}