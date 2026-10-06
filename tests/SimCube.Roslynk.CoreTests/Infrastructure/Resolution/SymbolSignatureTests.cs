using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Resolution;

public class SymbolSignatureTests
{
	private const string Source =
		"""
		namespace Sample
		{
			public class Target
			{
				public void M() { }
				public void M(int value) { }
				public void M(string value, int times) { }
				public void M(int? value) { }
				public void M(ref int value) { }
				public void M(string? value, bool flag) { }
				public void M<T>(T value) { }
				public void M(System.Collections.Generic.List<int> values) { }
				public int this[int index] => index;
			}

			public class Colliding
			{
				public void Pick(First.Thing thing) { }
				public void Pick(Second.Thing thing) { }
			}
		}

		namespace Sample.First { public class Thing { } }
		namespace Sample.Second { public class Thing { } }
		""";

	[Test]
	public async Task WhenAMethodIsRendered_ThenParameterTypesUseKeywordAliases()
	{
		await Assert.That(Render("M", parameterCount: 2, first: "string")).IsEqualTo("Sample.Target.M(string, int)");
		await Assert.That(Render("M", parameterCount: 0)).IsEqualTo("Sample.Target.M()");
	}

	[Test]
	public async Task WhenRenderedFullyQualified_ThenParameterTypesCarryTheirNamespace()
	{
		IMethodSymbol method = Method("M", parameterCount: 1, first: "List<int>");

		await Assert.That(SymbolSignature.Of(method, SignatureTier.FullyQualified)).IsEqualTo("Sample.Target.M(System.Collections.Generic.List<System.Int32>)");
	}

	[Test]
	public async Task WhenAnIndexerIsRendered_ThenBracketsAreUsed()
	{
		IPropertySymbol indexer = Type("Target").GetMembers().OfType<IPropertySymbol>().Single(member => member.IsIndexer);

		await Assert.That(SymbolSignature.Of(indexer)).IsEqualTo("Sample.Target.this[int]");
	}

	[Test]
	public async Task WhenAGenericMethodIsRendered_ThenItsTypeParametersArePresent()
	{
		IMethodSymbol method = Type("Target").GetMembers("M").OfType<IMethodSymbol>().Single(candidate => candidate.Arity == 1);

		await Assert.That(SymbolSignature.Of(method)).IsEqualTo("Sample.Target.M<T>(T)");
	}

	[Test]
	public async Task WhenAClassIsRendered_ThenNoParameterListIsAdded()
	{
		await Assert.That(SymbolSignature.Of(Type("Target"))).IsEqualTo("Sample.Target");
	}

	[Test]
	public async Task WhenOverloadsAreDistinguished_ThenEachRendersDifferently()
	{
		IReadOnlyList<string> candidates =
			SymbolSignature.Distinguish(Type("Target").GetMembers("M").OfType<IMethodSymbol>());

		await Assert.That(candidates.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(candidates.Count);
		await Assert.That(candidates).Contains("Sample.Target.M(int)");
		await Assert.That(candidates).Contains("Sample.Target.M(int?)");
	}

	[Test]
	public async Task WhenTypesCollideAcrossNamespaces_ThenCandidatesEscalateToFullyQualified()
	{
		IReadOnlyList<string> candidates =
			SymbolSignature.Distinguish(Type("Colliding").GetMembers("Pick").OfType<IMethodSymbol>());

		await Assert.That(candidates.Count).IsEqualTo(2);
		await Assert.That(candidates).Contains("Sample.Colliding.Pick(Sample.First.Thing)");
		await Assert.That(candidates).Contains("Sample.Colliding.Pick(Sample.Second.Thing)");
	}

	[Test]
	public async Task WhenASignatureIsWrittenWithParameterNamesAndDefaults_ThenTheyAreIgnored()
	{
		IMethodSymbol method = Method("M", parameterCount: 2, first: "string");

		await Assert.That(Matches(method, "Sample.Target.M(string value, int times = 3)")).IsTrue();
		await Assert.That(Matches(method, "Sample.Target.M( string , int )")).IsTrue();
	}

	[Test]
	public async Task WhenASignatureUsesFullyQualifiedParameterTypes_ThenItStillMatches()
	{
		IMethodSymbol method = Method("M", parameterCount: 1, first: "int");

		await Assert.That(Matches(method, "Sample.Target.M(System.Int32)")).IsTrue();
		await Assert.That(Matches(method, "Sample.Target.M(global::System.Int32)")).IsTrue();
	}

	[Test]
	public async Task WhenANullableReferenceAnnotationIsWritten_ThenMatchingIsUnaffected()
	{
		IMethodSymbol method = Method("M", parameterCount: 2, first: "string?");

		await Assert.That(Matches(method, "Sample.Target.M(string, bool)")).IsTrue();
		await Assert.That(Matches(method, "Sample.Target.M(string?, bool)")).IsTrue();
	}

	[Test]
	public async Task WhenANullableValueTypeIsWritten_ThenItDoesNotMatchTheNonNullableOverload()
	{
		IMethodSymbol nullable = Method("M", parameterCount: 1, first: "int?");
		IMethodSymbol plain = Method("M", parameterCount: 1, first: "int");

		await Assert.That(Matches(nullable, "Sample.Target.M(int?)")).IsTrue();
		await Assert.That(Matches(nullable, "Sample.Target.M(Nullable<int>)")).IsTrue();
		await Assert.That(Matches(nullable, "Sample.Target.M(int)")).IsFalse();
		await Assert.That(Matches(plain, "Sample.Target.M(int?)")).IsFalse();
	}

	[Test]
	public async Task WhenARefModifierIsWritten_ThenOnlyTheMatchingOverloadMatches()
	{
		IMethodSymbol byRef = Type("Target").GetMembers("M").OfType<IMethodSymbol>()
			.Single(candidate => candidate.Parameters.Length == 1 && candidate.Parameters[0].RefKind == RefKind.Ref);
		IMethodSymbol byValue = Method("M", parameterCount: 1, first: "int");

		await Assert.That(Matches(byRef, "Sample.Target.M(ref int)")).IsTrue();
		await Assert.That(Matches(byValue, "Sample.Target.M(ref int)")).IsFalse();

		// Written without a modifier, the name is modifier-agnostic and both overloads match.
		await Assert.That(Matches(byRef, "Sample.Target.M(int)")).IsTrue();
	}

	[Test]
	public async Task WhenNoParameterListIsWritten_ThenEveryOverloadMatches()
	{
		await Assert.That(SymbolSignature.TryParse("Sample.Target.M", out SymbolSignatureQuery query)).IsTrue();
		await Assert.That(query.ListKind).IsEqualTo(ParameterListKind.None);
		await Assert.That(Type("Target").GetMembers("M").OfType<IMethodSymbol>()).All(method => SymbolSignature.Matches(method, query));
	}

	[Test]
	public async Task WhenEmptyParenthesesAreWritten_ThenOnlyTheZeroArgOverloadMatches()
	{
		await Assert.That(Matches(Method("M", parameterCount: 0), "Sample.Target.M()")).IsTrue();
		await Assert.That(Matches(Method("M", parameterCount: 1, first: "int"), "Sample.Target.M()")).IsFalse();
	}

	[Test]
	public async Task WhenTheNameContainsDotsAndCommasInsideBrackets_ThenTheHeadSplitsAtTheOuterDot()
	{
		await Assert.That(SymbolSignature.TryParse("N.Repo.Get(System.Collections.Generic.Dictionary<string, int>)", out SymbolSignatureQuery query)).IsTrue();

		await Assert.That(query.QualifiedName).IsEqualTo("N.Repo.Get");
		await Assert.That(query.SimpleName).IsEqualTo("Get");
		await Assert.That(query.Parameters).HasSingleItem();
		await Assert.That(query.Parameters[0].TypeText).IsEqualTo("System.Collections.Generic.Dictionary<string, int>");
	}

	[Test]
	public async Task WhenAGenericArityIsWritten_ThenItIsParsedOffTheSimpleName()
	{
		await Assert.That(SymbolSignature.TryParse("N.Repo.Get<T>(T)", out SymbolSignatureQuery query)).IsTrue();

		await Assert.That(query.SimpleName).IsEqualTo("Get");
		await Assert.That(query.Arity).IsEqualTo(1);
	}

	[Test]
	public async Task WhenAGenericArityIsWritten_ThenOnlyAMethodOfThatArityMatches()
	{
		IMethodSymbol generic = Type("Target").GetMembers("M").OfType<IMethodSymbol>().Single(candidate => candidate.Arity == 1);

		await Assert.That(Matches(generic, "Sample.Target.M<T>(T)")).IsTrue();
		await Assert.That(Matches(Method("M", parameterCount: 1, first: "int"), "Sample.Target.M<T>(T)")).IsFalse();
	}

	[Test]
	public async Task WhenAnIndexerSignatureIsWritten_ThenParenthesesDoNotMatchIt()
	{
		IPropertySymbol indexer = Type("Target").GetMembers().OfType<IPropertySymbol>().Single(member => member.IsIndexer);

		await Assert.That(Matches(indexer, "Sample.Target.this[int]")).IsTrue();
		await Assert.That(Matches(indexer, "Sample.Target.this(int)")).IsFalse();
	}

	[Test]
	public async Task WhenTheNameIsBlankOrTheBracketsAreUnbalanced_ThenItDoesNotParse()
	{
		await Assert.That(SymbolSignature.TryParse(null, out _)).IsFalse();
		await Assert.That(SymbolSignature.TryParse("   ", out _)).IsFalse();
		await Assert.That(SymbolSignature.TryParse("N.T.M int)", out _)).IsFalse();
	}

	private static async Task<bool> Matches(ISymbol symbol, string name)
	{
		await Assert.That(SymbolSignature.TryParse(name, out SymbolSignatureQuery query)).IsTrue();
		return SymbolSignature.Matches(symbol, query);
	}

	private static string Render(string methodName, int parameterCount, string? first = null) =>
		SymbolSignature.Of(Method(methodName, parameterCount, first));

	/// <summary>
	/// The overload of <paramref name="name"/> with the given parameter count, narrowed by its first
	/// parameter's minimally-qualified type when several overloads share that count.
	/// </summary>
	private static IMethodSymbol Method(string name, int parameterCount, string? first = null) =>
		Type("Target").GetMembers(name)
			.OfType<IMethodSymbol>()
			.Where(candidate => candidate.Arity == 0)
			.Where(candidate => candidate.Parameters.Length == parameterCount)
			.Single(candidate => first is null
				|| (candidate.Parameters[0].RefKind == RefKind.None
					&& candidate.Parameters[0].Type.ToDisplayString(FirstParameterFormat) == first));

	private static readonly SymbolDisplayFormat FirstParameterFormat = new(
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
			| SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

	private static INamedTypeSymbol Type(string name) =>
		Compilation.GetTypeByMetadataName($"Sample.{name}")
		?? throw new InvalidOperationException($"'{name}' is missing from the test compilation.");

	private static readonly CSharpCompilation Compilation = CSharpCompilation.Create(
		"SymbolSignatureTests",
		[CSharpSyntaxTree.ParseText(Source)],
		[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
		new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
}