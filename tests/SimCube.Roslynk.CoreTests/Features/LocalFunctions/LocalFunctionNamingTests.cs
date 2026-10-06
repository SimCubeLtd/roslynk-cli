using TUnit.Assertions.Enums;
using System.IO;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.Callers.GetCallers;
using SimCube.Roslynk.Core.Features.MultiQuery;
using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Features.Refactorings.ExtractMethod;
using SimCube.Roslynk.Core.Features.Signatures.ChangeSignature;
using SimCube.Roslynk.Core.Features.Signatures.RenameParameter;
using SimCube.Roslynk.Core.Features.Symbols.FindDefinition;
using SimCube.Roslynk.Core.Features.Symbols.GetMembers;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbol;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody;
using SimCube.Roslynk.Core.Features.Symbols.SearchSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;
using SimCube.Roslynk.CoreTests.Features.MultiQuery;

namespace SimCube.Roslynk.CoreTests.Features.LocalFunctions;

/// <summary>
/// Local functions are named as members of the member declaring them, through a nested class and through a
/// local function declared inside another: LocalFunctionLib.ParentClass.Widget.Method1.localMethod.inner.
/// Every name-based tool resolves that form, and every name a tool emits for a local function is one it accepts.
/// </summary>
public class LocalFunctionNamingTests
{
	private const string Widget = "LocalFunctionLib.ParentClass.Widget";
	private const string LocalMethod = Widget + ".Method1.localMethod";
	private const string Inner = LocalMethod + ".inner";
	private const string LocalMethodSignature = Widget + ".Method1(int).localMethod(string, int)";
	private const string InnerSignature = LocalMethodSignature + ".inner(string)";
	private const string FixtureFile = "LocalFunctionLib/ParentClass.cs";

	// ---- naming and resolution ----

	[Test]
	public async Task WhenNamingALocalFunctionInsideALocalFunctionOfANestedClass_ThenEveryContainerIsQualified()
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);
		IMethodSymbol inner = await ResolveSingleAsync(solution, Inner);

		await Assert.That(inner.MethodKind).IsEqualTo(MethodKind.LocalFunction);
		await Assert.That(SymbolResolver.FullyQualifiedName(inner)).IsEqualTo(Inner);
		await Assert.That(SymbolResolver.SignatureName(inner)).IsEqualTo(InnerSignature);
		await Assert.That(SymbolKindText.Of(inner)).IsEqualTo("localfunction");
	}

	[Test]
	[Arguments(LocalMethod)]
	[Arguments(Widget + ".Method1(int).localMethod")]
	[Arguments(Widget + ".Method1.localMethod(string, int)")]
	[Arguments(LocalMethodSignature)]
	[Arguments(Widget + ".Method1(int count).localMethod(string p1, int p2)")]
	public async Task WhenResolvingALocalFunctionWithOrWithoutParameterLists_ThenItResolves(string name)
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);

		IMethodSymbol local = await ResolveSingleAsync(solution, name);

		await Assert.That(local.Name).IsEqualTo("localMethod");
		await Assert.That(SymbolResolver.SignatureName(local)).IsEqualTo(LocalMethodSignature);
	}

	[Test]
	[Arguments(Inner)]
	[Arguments(InnerSignature)]
	[Arguments(Widget + ".Method1(int).localMethod.inner(string)")]
	public async Task WhenResolvingALocalFunctionNestedInALocalFunction_ThenItResolves(string name)
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);

		IMethodSymbol inner = await ResolveSingleAsync(solution, name);

		await Assert.That(inner.Name).IsEqualTo("inner");
		await Assert.That(((IMethodSymbol)inner.ContainingSymbol).Name).IsEqualTo("localMethod");
	}

	[Test]
	[Arguments(Widget + ".Method1(string).localMethod")]
	[Arguments(Widget + ".Method1.localMethod(int)")]
	[Arguments(Widget + ".Method1.inner")]
	[Arguments(Widget + ".localMethod")]
	[Arguments(Widget + ".Method1.missing")]
	public async Task WhenTheContainerChainOrSignatureDoesNotMatch_ThenNothingResolves(string name)
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);

		IReadOnlyList<ISymbol> matches = await new SymbolResolver().FindByFullyQualifiedNameAsync(solution, name);

		await Assert.That(matches).IsEmpty();
	}

	[Test]
	public async Task WhenOverloadsOfTheContainerDeclareTheSameLocalName_ThenTheCandidatesQualifyTheContainerAndRoundTrip()
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);
		var resolver = new SymbolResolver();

		IReadOnlyList<ISymbol> matches = await resolver.FindByFullyQualifiedNameAsync(solution, Widget + ".Method2.helper");
		IReadOnlyList<string> candidates = SymbolSignature.Distinguish(matches);

		await Assert.That(candidates).IsEquivalentTo([Widget + ".Method2(int).helper(int)", Widget + ".Method2(string).helper(string)"], CollectionOrdering.Matching);
		foreach (string candidate in candidates)
		{
			IReadOnlyList<ISymbol> resolved = await resolver.FindByFullyQualifiedNameAsync(solution, candidate);
			await Assert.That(SymbolResolver.SignatureName(await Assert.That(resolved).HasSingleItem())).IsEqualTo(candidate);
		}
	}

	[Test]
	public async Task WhenResolvingABareLocalFunctionName_ThenItIsFound()
	{
		Solution solution = await LoadAsync(TestSolutions.LocalFunctions);

		IMethodSymbol inner = await ResolveSingleAsync(solution, "inner");

		await Assert.That(SymbolResolver.SignatureName(inner)).IsEqualTo(InnerSignature);
	}

	// ---- read tools ----

	[Test]
	public async Task WhenGettingTheBodyOfALocalFunctionInsideALocalFunction_ThenItsSourceIsReturnedVerbatim()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new GetSymbolBodyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbolBody(TestSolutions.LocalFunctions, Inner);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("static int inner(string text)\r\n\t\t\t\t{\r\n\t\t\t\t\treturn text.Length;\r\n\t\t\t\t}");
	}

	[Test]
	public async Task WhenGettingALocalFunctionSymbol_ThenItsKindAndDeclarationAreReported()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new GetSymbolTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbol(TestSolutions.LocalFunctions, LocalMethod);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("static int localMethod(string p1, int p2)");
	}

	[Test]
	public async Task WhenAnAmbiguousLocalFunctionIsRequested_ThenTheCandidatesAreTheQualifiedOverloads()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new GetSymbolBodyTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetSymbolBody(TestSolutions.LocalFunctions, Widget + ".Method2.helper");

		await Assert.That(result).Contains("error=Ambiguous");
		await Assert.That(result).Contains($"candidate={Widget}.Method2(int).helper(int)");
		await Assert.That(result).Contains($"candidate={Widget}.Method2(string).helper(string)");
	}

	[Test]
	public async Task WhenFindingReferencesToANestedLocalFunction_ThenTheReferenceNestsUnderItsContainingLocalFunction()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.LocalFunctions, Inner);

		await Assert.That(result).Contains($"resolvedSymbol={InnerSignature}");
		string[] lines = result.Split('\n');
		int method = Array.FindIndex(lines, line => line.Trim().StartsWith("method,Method1", StringComparison.Ordinal));
		int local = Array.FindIndex(lines, line => line.Trim().StartsWith("localfunction,localMethod", StringComparison.Ordinal));
		await Assert.That(method >= 0 && local > method).IsTrue();
		await Assert.That(lines[local]).Contains("13:");
	}

	[Test]
	public async Task WhenGettingTheCallersOfANestedLocalFunction_ThenTheCallingLocalFunctionNestsUnderItsMethod()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new GetCallersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetCallers(TestSolutions.LocalFunctions, Inner);

		await Assert.That(result).Contains($"resolvedSymbol={InnerSignature}");
		await Assert.That(result).Contains("class,ParentClass");
		await Assert.That(result).Contains("class,Widget");
		await Assert.That(result).Contains("method,Method1");
		await Assert.That(result).Contains("localfunction,localMethod,11:");
	}

	[Test]
	public async Task WhenSearchingForALocalFunctionName_ThenTheLocalFunctionIsListedUnderItsContainers()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new SearchSymbolsTool(registry, new ProjectionService());

		string result = await subject.SearchSymbols(TestSolutions.LocalFunctions, "inner");

		await Assert.That(result).Contains("localfunction,localMethod");
		await Assert.That(result).Contains("localfunction,inner,16:");
	}

	[Test]
	public async Task WhenFindingTheDefinitionOfANestedLocalFunctionCall_ThenItsFullNameIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new FindDefinitionTool(registry, new SymbolResolver(), new ProjectionService());

		// Line 13 is 'int length = inner(p1);'.
		string result = await subject.FindDefinition(TestSolutions.LocalFunctions, FixtureFile, 13, 19);

		await Assert.That(result).Contains($"fullName={InnerSignature}");
		await Assert.That(result).Contains("kind=localfunction");
	}

	[Test]
	public async Task WhenListingTheMembersOfANestedClass_ThenEachMembersLocalFunctionsNestBeneathIt()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var subject = new GetMembersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetMembers(TestSolutions.LocalFunctions, Widget);

		await Assert.That(result.Replace("\r\n", "\n")).IsEqualTo($"resolvedType={Widget}\n" +
			"\n" +
			"LocalFunctionLib\n" +
			"\tLocalFunctionLib\n" +
			"\t\tParentClass.cs\n" +
			"\t\t\tmethod,Method1,7:3-21:4,int\n" +
			"\t\t\t\tlocalfunction,localMethod,11:4-20:5,string|int\n" +
			"\t\t\t\t\tlocalfunction,inner,16:5-19:6,string\n" +
			"\t\t\tmethod,Method2,23:3-28:4,int\n" +
			"\t\t\t\tlocalfunction,helper,27:4-27:48,int\n" +
			"\t\t\tmethod,Method2,30:3-35:4,string\n" +
			"\t\t\t\tlocalfunction,helper,34:4-34:50,string\n" +
			"\t\t\tmethod,Caller,37:3-37:50\n");
	}

	[Test]
	public async Task WhenBatchingQueriesOnLocalFunctions_ThenEverySlotResolves()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.LocalFunctions);
		var provider = new ServiceCollection()
			.AddSingleton(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.AddSingleton<ConditionalCoverage>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		string envelope = await subject.MultiQuery(
			TestSolutions.LocalFunctions,
			[
				new(MultiQueryOp.get_symbol_body, MultiQueryTestHelpers.Args(("symbolName", Json(Inner)))),
				new(MultiQueryOp.find_references, MultiQueryTestHelpers.Args(("symbolName", Json(LocalMethodSignature)))),
				new(MultiQueryOp.get_callers, MultiQueryTestHelpers.Args(("methodName", Json(LocalMethod)))),
			]);

		await Assert.That(envelope).DoesNotContain("error=");
		await Assert.That(envelope).Contains("return text.Length;");
		await Assert.That(envelope).Contains($"resolvedSymbol={LocalMethodSignature}");
	}

	// ---- write tools ----

	[Test]
	public async Task WhenRenamingAParameterOfALocalFunctionInANestedClass_ThenTheDeclarationAndUsesAreRenamed()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameParameterTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameParameter(solutionPath, LocalMethod, "p1", "value");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains($"resolvedMethod={LocalMethodSignature}");
		string text = await ReadAsync(solutionPath);
		await Assert.That(text).Contains("static int localMethod(string value, int p2)");
		await Assert.That(text).Contains("int length = inner(value);");
		await Assert.That(text).Contains("static int inner(string text)");
	}

	[Test]
	public async Task WhenRenamingAParameterOfALocalFunctionInsideALocalFunction_ThenOnlyThatFunctionChanges()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameParameterTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameParameter(solutionPath, Inner, "text", "word");

		await Assert.That(result).Contains("applied=Y");
		string text = await ReadAsync(solutionPath);
		await Assert.That(text).Contains("static int inner(string word)");
		await Assert.That(text).Contains("return word.Length;");
		await Assert.That(text).Contains("static int helper(string text) => text.Length;");
	}

	[Test]
	public async Task WhenRenamingAParameterOfAnAmbiguousLocalFunction_ThenTheOverloadCandidatesAreReturned()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameParameterTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());
		string before = await ReadAsync(solutionPath);

		string ambiguous = await subject.RenameParameter(solutionPath, Widget + ".Method2.helper", "text", "value");
		string targeted = await subject.RenameParameter(solutionPath, Widget + ".Method2(string).helper(string)", "text", "value");

		await Assert.That(ambiguous).Contains("error=Ambiguous");
		await Assert.That(targeted).Contains("applied=Y");
		string text = await ReadAsync(solutionPath);
		await Assert.That(text).IsNotEqualTo(before);
		await Assert.That(text).Contains("static int helper(string value) => value.Length;");
		await Assert.That(text).Contains("static int helper(int number) => number * 2;");
	}

	[Test]
	public async Task WhenRenamingALocalFunctionInsideALocalFunction_ThenItsDeclarationAndCallAreRenamed()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(solutionPath, Inner, "Measure");

		await Assert.That(result).Contains("applied=Y");
		string text = await ReadAsync(solutionPath);
		await Assert.That(text).Contains("int length = Measure(p1);");
		await Assert.That(text).Contains("static int Measure(string text)");
		await Assert.That(text).DoesNotContain("inner");
	}

	[Test]
	public async Task WhenAddingAParameterToALocalFunctionInsideALocalFunction_ThenItsCallSiteIsUpdated()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(solutionPath, Inner, "int", "factor", "1", callSiteArgument: "2");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("updatedCallSites=1");
		await Assert.That(result).Contains($"resolvedMethod={InnerSignature}");
		string text = await ReadAsync(solutionPath);
		await Assert.That(text).Contains("static int inner(string text, int factor = 1)");
		await Assert.That(text).Contains("int length = inner(p1, factor: 2);");
	}

	[Test]
	public async Task WhenExtractingALocalFunctionFromALocalFunctionInsideALocalFunction_ThenItsFullNameIsReported()
	{
		string solutionPath = TestSolutions.CreateScratchLocalFunctionSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());

		// Line 18 is '\t\t\t\t\treturn text.Length;'; select the expression 'text.Length'.
		string result = await subject.ExtractMethod(solutionPath, FixtureFile, 18, 13, 18, 24, methodName: "LengthOf", asLocalFunction: true);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("kind=LocalFunction");
		await Assert.That(result).Contains($"symbolName={InnerSignature}.LengthOf(string)");

		// The reported name resolves straight back to the new local function.
		Solution solution = registry.GetOrBegin(solutionPath).CurrentModel.Solution!;
		IMethodSymbol extracted = await ResolveSingleAsync(solution, $"{Inner}.LengthOf");
		await Assert.That(extracted.MethodKind).IsEqualTo(MethodKind.LocalFunction);
	}

	// ---- helpers ----

	private static async Task<Solution> LoadAsync(string solutionPath)
	{
		var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(solutionPath);
		return instance.CurrentModel.Solution!;
	}

	private static async Task<IMethodSymbol> ResolveSingleAsync(Solution solution, string name)
	{
		IReadOnlyList<ISymbol> matches = await new SymbolResolver().FindByFullyQualifiedNameAsync(solution, name);
		ISymbol match = await Assert.That(matches).HasSingleItem();
		await Assert.That(match).IsAssignableTo<IMethodSymbol>();
		return (IMethodSymbol)match;
	}

	private static Task<string> ReadAsync(string solutionPath) =>
		File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(solutionPath)!, "LocalFunctionLib", "ParentClass.cs"));

	private static JsonElement Json(string value) => MultiQueryTestHelpers.Json(value);
}
