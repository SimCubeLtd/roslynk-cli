using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Diagnostics;

public class AnalyzerDriverFactoryTests
{
	[Test]
	public async Task WhenAnAnalyzerThrows_ThenTheDriverStillReturns()
	{
		// A third-party analyzer must not be able to fail a tool call, so the driver is always built with an
		// exception handler; without one Roslyn would surface the fault to the caller.
		Compilation compilation = Compile("class Thing { }");
		CompilationWithAnalyzers driver = AnalyzerDriverFactory.Create(
			compilation,
			[new ThrowingAnalyzer()],
			new AnalyzerOptions([]));
		SyntaxTree tree = compilation.SyntaxTrees.Single();

		ImmutableArray<Diagnostic> diagnostics = await driver.GetAnalyzerSemanticDiagnosticsAsync(
			compilation.GetSemanticModel(tree), filterSpan: null, CancellationToken.None);

		await Assert.That(diagnostics).IsEmpty();
	}

	[Test]
	public async Task WhenAnAnalyzerReportsNothingFixable_ThenItIsNarrowedAway()
	{
		Compilation compilation = Compile("class Thing { }");

		ImmutableArray<DiagnosticAnalyzer> narrowed = AnalyzerDriverFactory.Narrow(
			[new ThrowingAnalyzer()],
			compilation,
			compilation.SyntaxTrees.Single(),
			["CS0219"],
			CancellationToken.None);

		await Assert.That(narrowed).IsEmpty();
	}

	[Test]
	public async Task WhenAnAnalyzerReportsAFixableId_ThenItIsKept()
	{
		Compilation compilation = Compile("class Thing { }");

		ImmutableArray<DiagnosticAnalyzer> narrowed = AnalyzerDriverFactory.Narrow(
			[new ThrowingAnalyzer()],
			compilation,
			compilation.SyntaxTrees.Single(),
			[ThrowingAnalyzer.Id],
			CancellationToken.None);

		await Assert.That(narrowed).HasSingleItem();
	}

	private static Compilation Compile(string source) =>
		CSharpCompilation.Create(
			"Probe",
			[CSharpSyntaxTree.ParseText(source)],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

	// RS1001/RS2008 are for analyzers a project ships; this one is constructed by the test and handed to
	// the driver, and marking it as a real analyzer would instead make Roslyn treat the whole test assembly
	// as an analyzer package (RS1036/RS1038/RS1041).
#pragma warning disable RS1001, RS2008
	/// <summary>
	/// Deliberately not marked with <see cref="DiagnosticAnalyzerAttribute"/>: it is handed to the driver
	/// directly, and the attribute would make Roslyn's analyzer-authoring rules treat this test assembly as
	/// a shipped analyzer.
	/// </summary>
	private sealed class ThrowingAnalyzer : DiagnosticAnalyzer
	{
		public const string Id = "ZZ0001";

		private static readonly DiagnosticDescriptor Descriptor =
			new(Id, "Throws", "Throws", "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true);

		public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

		public override void Initialize(AnalysisContext context)
		{
			context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
			context.EnableConcurrentExecution();
			context.RegisterSemanticModelAction(_ => throw new InvalidOperationException("Deliberate analyzer fault."));
		}
	}
#pragma warning restore RS1001, RS2008
}