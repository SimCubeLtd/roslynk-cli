using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Documentation;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Documentation;

public class DocumentationReaderTests
{
	[Test]
	public async Task WhenASymbolHasOwnDocs_ThenSectionsAndInlineTagsAreNormalized()
	{
		ISymbol symbol = await ResolveAsync("SimpleLibrary.Widget.Compute");

		SymbolDocumentation documentation = DocumentationReader.Read(symbol);

		await Assert.That(documentation.Source).IsEqualTo("own");
		await Assert.That(documentation.Summary).IsNotNull();
		await Assert.That(documentation.Summary!).Contains("`value`");
		await Assert.That(documentation.Summary!).Contains("`Int32`");
		await Assert.That(documentation.Returns).IsEqualTo("Twice the input.");
		DocumentationParam parameter = await Assert.That(documentation.Params).HasSingleItem();
		await Assert.That(parameter.Name).IsEqualTo("value");
	}

	[Test]
	public async Task WhenAMemberUsesInheritDoc_ThenDocsComeFromTheInterface()
	{
		ISymbol symbol = await ResolveAsync("SimpleLibrary.Greeter.Greet");

		SymbolDocumentation documentation = DocumentationReader.Read(symbol);

		await Assert.That(documentation.Source).IsEqualTo("inherited");
		await Assert.That(documentation.InheritedFrom).IsNotNull();
		await Assert.That(documentation.InheritedFrom!.Symbol).IsEqualTo("SimpleLibrary.IGreeter.Greet");
		await Assert.That(documentation.InheritedFrom.SourcePath).EndsWith("IGreeter.cs");
		await Assert.That(documentation.Summary!).Contains("`name`");
	}

	[Test]
	public async Task WhenASymbolHasNoDocs_ThenSourceIsNone()
	{
		ISymbol symbol = await ResolveAsync("SimpleLibrary.Caller.Run");

		SymbolDocumentation documentation = DocumentationReader.Read(symbol);

		await Assert.That(documentation.Source).IsEqualTo("none");
		await Assert.That(documentation.Summary).IsNull();
	}

	private static async Task<ISymbol> ResolveAsync(string fullyQualifiedName)
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Simple);
		return (await new SymbolResolver().FindByFullyQualifiedNameAsync(instance.CurrentSolution, fullyQualifiedName)).Single();
	}
}