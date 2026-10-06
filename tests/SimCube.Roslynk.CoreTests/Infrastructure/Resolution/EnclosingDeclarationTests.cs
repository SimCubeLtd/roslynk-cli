using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Resolution;

public class EnclosingDeclarationTests
{
	[Test]
	public async Task WhenAReferenceSitsInsideAMethod_ThenThePathEndsAtTheMethodNestedInItsType()
	{
		EnclosingPath path = await ResolveFirstReferenceAsync("SimpleLibrary.Greeter");

		await Assert.That(path.Namespace).IsEqualTo("SimpleLibrary");

		EnclosingSegment leaf = path.Segments[^1];
		await Assert.That(leaf.Kind).IsEqualTo("method");
		await Assert.That(leaf.Name).IsEqualTo("Run");
		await Assert.That(path.Segments).Contains(segment => segment.Kind == "class" && segment.Name == "Caller");
	}

	[Test]
	public async Task WhenAReferenceSitsAtTypeLevel_ThenThePathEndsAtTheType()
	{
		EnclosingPath path = await ResolveFirstReferenceAsync("SimpleLibrary.IGreeter");

		await Assert.That(path.Namespace).IsEqualTo("SimpleLibrary");

		EnclosingSegment leaf = path.Segments[^1];
		await Assert.That(leaf.Kind).IsEqualTo("class");
		await Assert.That(leaf.Name).IsEqualTo("Greeter");
	}

	private static async Task<EnclosingPath> ResolveFirstReferenceAsync(string symbolName)
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Simple);
		Solution solution = instance.CurrentSolution;

		ISymbol symbol = (await new SymbolResolver().FindByFullyQualifiedNameAsync(solution, symbolName))[0];
		Location location = (await SymbolFinder.FindReferencesAsync(symbol, solution))
			.SelectMany(referenced => referenced.Locations)
			.First(reference => reference.Location.IsInSource)
			.Location;

		return await EnclosingDeclaration.ResolveAsync(solution, location);
	}
}