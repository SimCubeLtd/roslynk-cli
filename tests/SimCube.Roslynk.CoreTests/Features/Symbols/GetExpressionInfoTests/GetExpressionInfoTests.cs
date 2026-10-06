using System.IO;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.MultiQuery;
using SimCube.Roslynk.Core.Features.Symbols.GetExpressionInfo;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.GetExpressionInfoTests;

public class GetExpressionInfoTests
{
	private static string SamplesPath => Path.Combine(Path.GetDirectoryName(TestSolutions.Expressions)!, "ExpressionLib", "Samples.cs");

	[Test]
	public async Task WhenPositionedOnAnOverloadedCall_ThenTheSelectedOverloadAndReturnTypeAreReported()
	{
		string intCall = await QueryAsync("Format(42)");
		await Assert.That(intCall).DoesNotContain("error=");
		await Assert.That(intCall).Contains("expr=Formatter.Format(42)\n");
		await Assert.That(intCall).Contains("exprKind=InvocationExpression\n");
		await Assert.That(intCall).Contains("type=string\n");
		await Assert.That(intCall).Contains("symbol=ExpressionLib.Formatter.Format(int)\n");
		await Assert.That(intCall).Contains("symbolKind=method\n");
		await Assert.That(intCall).Contains("origin=source\n");
		await Assert.That(intCall).Contains("symbolPath=ExpressionLib/Samples.cs\n");
		await Assert.That(intCall).Contains("doc=Formats an Int32 value.\n");
		await Assert.That(intCall).Contains("constant=none\n");

		string stringCall = await QueryAsync("Format(\"x\")");
		await Assert.That(stringCall).Contains("symbol=ExpressionLib.Formatter.Format(string)\n");
	}

	[Test]
	public async Task WhenPositionedOnVar_ThenTheInferredTypeIsReported()
	{
		string result = await QueryAsync("var text");

		await Assert.That(result).Contains("exprKind=IdentifierName\n");
		// 'var' is always inferred nullable-annotated for a reference type.
		await Assert.That(result).Contains("type=string?\n");
		await Assert.That(result).Contains("symbol=System.String\n");
		await Assert.That(result).Contains("origin=metadata\n");
	}

	[Test]
	public async Task WhenPositionedOnAConstantWidenedToLong_ThenTheConstantAndImplicitConversionAreReported()
	{
		string result = await QueryAsync("Width;", offset: 0);

		await Assert.That(result).Contains("expr=Formatter.Width\n");
		await Assert.That(result).Contains("symbol=ExpressionLib.Formatter.Width\n");
		await Assert.That(result).Contains("symbolKind=field\n");
		await Assert.That(result).Contains("type=int\n");
		await Assert.That(result).Contains("convertedType=long\n");
		await Assert.That(result).Contains("constant=40\n");
		await Assert.That(result).Matches("conversion=implicit[^\n]* numeric");
	}

	[Test]
	public async Task WhenPositionedOnAUserDefinedConversionSource_ThenTheOperatorIsReported()
	{
		string result = await QueryAsync("Meters(3)");

		await Assert.That(result).Contains("exprKind=ObjectCreationExpression\n");
		await Assert.That(result).Contains("type=ExpressionLib.Meters\n");
		await Assert.That(result).Contains("convertedType=double\n");
		await Assert.That(result).Contains("symbol=ExpressionLib.Meters.Meters(double)\n");
		await Assert.That(result).Contains("conversion=implicit user-defined ExpressionLib.Meters.implicit operator double(ExpressionLib.Meters)");
	}

	[Test]
	public async Task WhenPositionedOnAGenericOrExtensionCall_ThenTheDefinitionAndItsInstantiationAreReported()
	{
		string generic = await QueryAsync("Echo(text)");
		await Assert.That(generic).Contains("symbol=ExpressionLib.Formatter.Echo<T>(T)\n");
		await Assert.That(generic).Contains("instantiation=");
		await Assert.That(generic).Contains("type=string\n");

		string extension = await QueryAsync("Twice()");
		await Assert.That(extension).Contains("symbol=ExpressionLib.Formatter.Twice(int)\n");
		await Assert.That(extension).Contains("instantiation=");
		await Assert.That(extension).Contains("type=int\n");
	}

	[Test]
	public async Task WhenPositionedOnANullableProperty_ThenTheFlowStateReflectsTheNullCheck()
	{
		string checkedAccess = await QueryAsync("MaybeName.Length");
		await Assert.That(checkedAccess).Contains("expr=MaybeName\n");
		await Assert.That(checkedAccess).Contains("symbol=ExpressionLib.Samples.MaybeName\n");
		await Assert.That(checkedAccess).Contains("/NotNull\n");

		string unchecked_ = await QueryAsync("MaybeName;");
		await Assert.That(unchecked_).Contains("/MaybeNull\n");
	}

	[Test]
	public async Task WhenTheCallCannotBeResolved_ThenNoSymbolIsGuessedAndCandidatesAreListed()
	{
		string result = await QueryAsync("Format(1.5)");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("symbol=none\n");
		await Assert.That(result).Contains("candidateReason=OverloadResolutionFailure\n");
		await Assert.That(result).Contains("ExpressionLib.Formatter.Format(int)");
		await Assert.That(result).Contains("ExpressionLib.Formatter.Format(string)");
	}

	[Test]
	public async Task WhenPositionedOnAVarLocalsName_ThenTheDeclaredLocalAndItsInferredTypeAreReported()
	{
		string result = await QueryAsync("text =");

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("expr=text\n");
		await Assert.That(result).Contains("exprKind=VariableDeclarator\n");
		await Assert.That(result).Contains("declaration=Y\n");
		await Assert.That(result).Contains("type=string?\n");
		await Assert.That(result).Contains("nullability=Annotated/NotNull\n");
		await Assert.That(result).Contains("symbol=text\n");
		await Assert.That(result).Contains("symbolKind=local\n");
		await Assert.That(result).Contains("origin=source\n");
	}

	[Test]
	public async Task WhenPositionedOnAConstantFieldsOrMethodsName_ThenTheDeclaredMemberIsReported()
	{
		string field = await QueryAsync("Width =");
		await Assert.That(field).Contains("declaration=Y\n");
		await Assert.That(field).Contains("type=int\n");
		await Assert.That(field).Contains("constant=40\n");
		await Assert.That(field).Contains("symbol=ExpressionLib.Formatter.Width\n");

		string method = await QueryAsync("Run()");
		await Assert.That(method).Contains("declaration=Y\n");
		await Assert.That(method).Contains("exprKind=MethodDeclaration\n");
		await Assert.That(method).Contains("type=void\n");
		await Assert.That(method).Contains("symbol=ExpressionLib.Samples.Run()\n");
		await Assert.That(method).Contains("symbolKind=method\n");
	}

	[Test]
	public async Task WhenPositionedOnAKeyword_ThenNotFoundIsReported()
	{
		string result = await QueryAsync("public void Run");

		await Assert.That(result).Contains("error=NotFound");
		await Assert.That(result).Contains("keywords");
	}

	[Test]
	public async Task WhenPositionedInARazorCodeBlock_ThenTheExpressionIsReportedAtItsRazorLocation()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Razor);
		var subject = new GetExpressionInfoTool(registry, new ProjectionService());
		string counterPath = Path.Combine(Path.GetDirectoryName(TestSolutions.Razor)!, "RazorLib", "Counter.razor");

		// Line 15 is 'CurrentCount = StartAt;'; column 18 is inside 'StartAt'.
		string result = await subject.GetExpressionInfo(TestSolutions.Razor, counterPath, 15, 18);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("expr=StartAt\n");
		await Assert.That(result).Contains("type=int\n");
		await Assert.That(result).Contains(".Counter.StartAt\n");
		await Assert.That(result).Contains("path=RazorLib/Counter.razor\n");
		await Assert.That(result).Contains("loc=15:");
	}

	[Test]
	public async Task WhenTheFileIsNotInTheSolution_ThenNotFoundIsReported()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Expressions);
		var subject = new GetExpressionInfoTool(registry, new ProjectionService());

		string result = await subject.GetExpressionInfo(TestSolutions.Expressions, "Missing.cs", 1, 1);

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetExpressionInfoTool(registry, new ProjectionService());

		string result = await subject.GetExpressionInfo(TestSolutions.Expressions, SamplesPath, 1, 1);

		await Assert.That(result).Contains("error=Indexing");

		await registry.GetOrAddAsync(TestSolutions.Expressions);
	}

	[Test]
	public async Task WhenRunThroughMultiQuery_ThenEachSlotReportsItsExpression()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Expressions);
		var provider = new ServiceCollection()
			.AddSingleton(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.AddSingleton<ConditionalCoverage>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		(int intLine, int intColumn) = await PositionOfAsync("Format(42)", 0);
		(int stringLine, int stringColumn) = await PositionOfAsync("Format(\"x\")", 0);
		var operations = new List<MultiQueryOperation>
		{
			new(MultiQueryOp.get_expression_info, Args(SamplesPath, intLine, intColumn)),
			new(MultiQueryOp.get_expression_info, Args(SamplesPath, stringLine, stringColumn)),
		};

		string envelope = await subject.MultiQuery(TestSolutions.Expressions, operations);

		await Assert.That(envelope).Contains("slot=1 tool=get_expression_info");
		await Assert.That(envelope).Contains("symbol=ExpressionLib.Formatter.Format(int)");
		await Assert.That(envelope).Contains("symbol=ExpressionLib.Formatter.Format(string)");
	}

	private static IReadOnlyDictionary<string, JsonElement> Args(string filePath, int line, int column) =>
		new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			["filePath"] = JsonSerializer.SerializeToElement(filePath),
			["line"] = JsonSerializer.SerializeToElement(line),
			["column"] = JsonSerializer.SerializeToElement(column),
		};

	private static async Task<string> QueryAsync(string anchor, int offset = 0)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Expressions);
		var subject = new GetExpressionInfoTool(registry, new ProjectionService());

		(int line, int column) = await PositionOfAsync(anchor, offset);
		return await subject.GetExpressionInfo(TestSolutions.Expressions, SamplesPath, line, column);
	}

	/// <summary>1-based line and column of <paramref name="offset"/> characters into the only occurrence of <paramref name="anchor"/>.</summary>
	private static async Task<(int Line, int Column)> PositionOfAsync(string anchor, int offset)
	{
		string text = await File.ReadAllTextAsync(SamplesPath);
		int index = text.IndexOf(anchor, StringComparison.Ordinal);
		await Assert.That(index >= 0 && text.IndexOf(anchor, index + 1, StringComparison.Ordinal) < 0).IsTrue().Because($"Anchor '{anchor}' must occur exactly once.");
		index += offset;

		int line = 1 + text[..index].Count(c => c == '\n');
		int column = index - (text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1) + 1;
		return (line, column);
	}
}