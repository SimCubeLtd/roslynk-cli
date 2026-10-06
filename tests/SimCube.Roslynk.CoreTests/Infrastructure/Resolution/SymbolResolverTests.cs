using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Resolution;

public class SymbolResolverTests
{
	[Test]
	public async Task WhenASignatureQualifiedNameIsGiven_ThenExactlyOneOverloadResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Ledger.Add(int)");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(SymbolSignature.Of(match)).IsEqualTo("SimpleLibrary.Ledger.Add(int)");
	}

	[Test]
	public async Task WhenABareNameIsGiven_ThenEveryOverloadResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Ledger.Add");

		await Assert.That(matches.Count).IsEqualTo(2);
	}

	[Test]
	public async Task WhenEmptyParenthesesAreGiven_ThenOnlyTheZeroArgOverloadResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Overloads.Pick()");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(((IMethodSymbol)match).Parameters).IsEmpty();
	}

	[Test]
	public async Task WhenFullyQualifiedParameterTypesAreGiven_ThenTheSameOverloadResolves()
	{
		IReadOnlyList<ISymbol> minimal = await ResolveAsync("SimpleLibrary.Overloads.Pick(string, int)");
		IReadOnlyList<ISymbol> qualified = await ResolveAsync("SimpleLibrary.Overloads.Pick(System.String, System.Int32)");

		await Assert.That(minimal).HasSingleItem();
		await Assert.That(qualified).HasSingleItem();
		await Assert.That(SymbolSignature.Of(qualified[0])).IsEqualTo(SymbolSignature.Of(minimal[0]));
	}

	[Test]
	public async Task WhenANullableValueTypeIsGiven_ThenTheNonNullableOverloadIsNotMatched()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Overloads.Pick(int?)");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(SymbolSignature.Of(match)).IsEqualTo("SimpleLibrary.Overloads.Pick(int?)");
	}

	[Test]
	public async Task WhenARefModifierIsGiven_ThenTheByRefOverloadResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Overloads.Pick(ref int)");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(((IMethodSymbol)match).Parameters[0].RefKind).IsEqualTo(RefKind.Ref);
	}

	[Test]
	public async Task WhenAGenericArityIsGiven_ThenTheGenericOverloadResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Overloads.Pick<T>(T)");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(((IMethodSymbol)match).Arity).IsEqualTo(1);
	}

	[Test]
	public async Task WhenAnIndexerSignatureIsGiven_ThenThatIndexerResolves()
	{
		IReadOnlyList<ISymbol> matches = await ResolveAsync("SimpleLibrary.Overloads.this[string]");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(((IPropertySymbol)match).IsIndexer).IsTrue();
		await Assert.That(SymbolSignature.Of(match)).IsEqualTo("SimpleLibrary.Overloads.this[string]");
	}

	[Test]
	public async Task WhenAMetadataMemberSignatureIsGiven_ThenOnlyThatOverloadResolves()
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Simple);

		IReadOnlyList<ISymbol> matches = await new SymbolResolver()
			.FindByFullyQualifiedNameWithMetadataAsync(instance.CurrentSolution, "System.String.Substring(int)");

		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(SymbolSignature.Of(match)).IsEqualTo("System.String.Substring(int)");
	}

	[Test]
	public async Task WhenTheNameDoesNotMatch_ThenNothingResolves()
	{
		await Assert.That(await ResolveAsync("SimpleLibrary.Ledger.Add(string)")).IsEmpty();
		await Assert.That(await ResolveAsync("SimpleLibrary.Ledger.Missing")).IsEmpty();
	}

	private static async Task<IReadOnlyList<ISymbol>> ResolveAsync(string symbolName)
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Simple);

		return await new SymbolResolver().FindByFullyQualifiedNameAsync(instance.CurrentSolution, symbolName);
	}
}