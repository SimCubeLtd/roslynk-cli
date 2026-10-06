using SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.GetTypeHierarchyTests;

public class GetTypeHierarchyTests
{
	[Test]
	public async Task WhenATypeImplementsAnInterface_ThenTheInterfaceIsInTheHierarchy()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetTypeHierarchyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetTypeHierarchy(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("resolvedType=SimpleLibrary.Greeter");
		await Assert.That(result).Contains("interfaces\n");
		await Assert.That(result).Contains("interface,SimpleLibrary.IGreeter");
	}

	[Test]
	public async Task WhenASectionIsEmpty_ThenItIsOmitted()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetTypeHierarchyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetTypeHierarchy(TestSolutions.Simple, "SimpleLibrary.Greeter");

		// Greeter has no derived types, so the 'derived' section header is absent entirely.
		await Assert.That(result).DoesNotContain("derived");
	}

	[Test]
	public async Task WhenTheTypeIsNotFound_ThenNotFoundIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetTypeHierarchyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetTypeHierarchy(TestSolutions.Simple, "SimpleLibrary.DoesNotExist");

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetTypeHierarchyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetTypeHierarchy(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
}