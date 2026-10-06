using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Projections;

public class ProjectionServiceKeyOfTests
{
	[Test]
	public async Task WhenOverloadsAreKeyed_ThenTheKeysDiffer()
	{
		IReadOnlyList<ISymbol> overloads = await ResolveAsync("SimpleLibrary.Ledger.Add");

		await Assert.That(overloads.Count).IsEqualTo(2);
		await Assert.That(overloads.Select(ProjectionService.KeyOf).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(2);
	}

	[Test]
	public async Task WhenAByRefOverloadIsKeyed_ThenItDiffersFromTheByValueOverload()
	{
		IReadOnlyList<ISymbol> byRef = await ResolveAsync("SimpleLibrary.Overloads.Pick(ref int)");
		IReadOnlyList<ISymbol> byValue = await ResolveAsync("SimpleLibrary.Overloads.Pick(int)");

		await Assert.That(ProjectionService.KeyOf(byValue[0])).IsNotEqualTo(ProjectionService.KeyOf(byRef[0]));
	}

	[Test]
	public async Task WhenTheSameMemberIsResolvedInSeveralProjections_ThenTheKeysMatch()
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Conditional);

		var projectionService = new ProjectionService();
		IReadOnlyList<Projection> projections = await projectionService.BuildAsync(instance.CurrentSolution);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups =
			await projectionService.ResolveAsync(new SymbolResolver(), projections, "ConditionalLib.Target.Ping");

		// Grouping is what proves the keys matched: one group, several per-projection instances inside it.
		IReadOnlyList<ProjectionSymbol> group = await Assert.That(groups).HasSingleItem();
		await Assert.That(group.Count > 1).IsTrue();
		await Assert.That(group.Select(instance => ProjectionService.KeyOf(instance.Symbol)).Distinct(StringComparer.Ordinal)).HasSingleItem();
	}

	[Test]
	public async Task WhenAKeyIsSentBackAsAName_ThenItResolvesToThatOneSymbol()
	{
		IReadOnlyList<ISymbol> overloads = await ResolveAsync("SimpleLibrary.Ledger.Add");

		foreach (ISymbol overload in overloads)
		{
			IReadOnlyList<ISymbol> resolved = await ResolveAsync(ProjectionService.KeyOf(overload));

			ISymbol match = await Assert.That(resolved).HasSingleItem();
			await Assert.That(ProjectionService.KeyOf(match)).IsEqualTo(ProjectionService.KeyOf(overload));
		}
	}

	private static async Task<IReadOnlyList<ISymbol>> ResolveAsync(string symbolName)
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Simple);

		return await new SymbolResolver().FindByFullyQualifiedNameAsync(instance.CurrentSolution, symbolName);
	}
}