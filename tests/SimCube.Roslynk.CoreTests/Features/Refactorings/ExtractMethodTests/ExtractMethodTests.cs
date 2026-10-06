using System.IO;
using SimCube.Roslynk.Core.Features.Refactorings.ExtractMethod;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.Refactorings.ExtractMethodTests;

public class ExtractMethodTests
{
	private const string Content =
		"namespace SimpleLibrary;\r\n\r\n" +
		"public class Greeter : IGreeter\r\n{\r\n" +
		"\tpublic string Greet(string name) => $\"Hello, {name}!\";\r\n\r\n" +
		"\tpublic int Sum(int a, int b)\r\n\t{\r\n" +
		"\t\tint total = a + b;\r\n" +
		"\t\ttotal *= 2;\r\n" +
		"\t\treturn total;\r\n" +
		"\t}\r\n\r\n" +
		"\tpublic async System.Threading.Tasks.Task<int> LaterAsync(int value)\r\n\t{\r\n" +
		"\t\tawait System.Threading.Tasks.Task.Delay(value);\r\n" +
		"\t\treturn value;\r\n" +
		"\t}\r\n}\r\n";

	[Test]
	public async Task WhenExtractingStatementsWithAName_ThenTheMethodIsCreatedAndCalled()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int endLine) = Lines("int total = a + b;", "total *= 2;");

		string result = await subject.ExtractMethod(solutionPath, greeter, startLine, 1, endLine + 1, 1, methodName: "DoubleSum");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("method=DoubleSum");
		await Assert.That(result).Contains("kind=Method");
		await Assert.That(result).Contains("signature=private static int DoubleSum(int a, int b)");
		await Assert.That(result).Contains("call=int total = DoubleSum(a, b);");
		await Assert.That(result).Contains("Greeter.cs");

		string written = await File.ReadAllTextAsync(greeter);
		await Assert.That(written).Contains("DoubleSum(a, b)");
		await Assert.That(written).Contains("private static int DoubleSum(int a, int b)");
		await Assert.That(written).DoesNotContain("NewMethod");
		await Assert.That(written).Matches(@"int total = DoubleSum\(a, b\);\r?\n\s*return total;");
		await Assert.That(written).Contains("\tpublic string Greet(string name) => $\"Hello, {name}!\";\r\n");

		RoslynInstance instance = await registry.GetOrAddAsync(solutionPath);
		await Assert.That((await instance.CurrentSolution.Projects.SelectMany(project => project.Documents)
			.First(document => document.FilePath == greeter).GetTextAsync()).ToString()).Contains("DoubleSum");
	}

	[Test]
	public async Task WhenCheckOnly_ThenThePreviewIsReturnedAndNothingIsWritten()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int endLine) = Lines("int total = a + b;", "total *= 2;");

		string result = await subject.ExtractMethod(solutionPath, greeter, startLine, 1, endLine + 1, 1, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result).Contains("method=NewMethod");
		await Assert.That(result).Contains("Greeter.cs");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(Content);
	}

	[Test]
	public async Task WhenExtractingAnExpression_ThenTheExpressionIsReplacedByTheCall()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int line, int column) = Position("a + b");

		string result = await subject.ExtractMethod(solutionPath, greeter, line, column, line, column + "a + b".Length, methodName: "Add");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("call=int total = Add(a, b);");
	}

	[Test]
	public async Task WhenAskedForALocalFunction_ThenALocalFunctionIsCreated()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int endLine) = Lines("int total = a + b;", "total *= 2;");

		string result = await subject.ExtractMethod(solutionPath, greeter, startLine, 1, endLine + 1, 1, methodName: "Local", asLocalFunction: true);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("kind=LocalFunction");
		await Assert.That(result).Contains("signature=static int Local(int a, int b)");
	}

	[Test]
	public async Task WhenTheSelectionAwaits_ThenTheExtractedMethodIsAsync()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int line, _) = Position("await System.Threading.Tasks.Task.Delay(value);");

		string result = await subject.ExtractMethod(solutionPath, greeter, line, 1, line + 1, 1, methodName: "WaitAsync");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result.Split('\n').First(header => header.StartsWith("signature=", StringComparison.Ordinal))).Contains("async");
		await Assert.That(await File.ReadAllTextAsync(greeter)).Contains("await WaitAsync(value);");
	}

	[Test]
	public async Task WhenTheSelectionCannotBeExtracted_ThenNotSupportedIsReturnedAndNothingIsWritten()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int endLine) = Lines("return total;", "await System.Threading.Tasks.Task.Delay(value);");

		// The selection spans the end of one method and the start of another.
		string result = await subject.ExtractMethod(solutionPath, greeter, startLine, 1, endLine + 1, 1);

		await Assert.That(result).Contains("error=NotSupported");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(Content);
	}

	[Test]
	public async Task WhenTheNameWouldIntroduceACompileError_ThenNothingIsWritten()
	{
		(string solutionPath, string greeter) = CreateScenario();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int endLine) = Lines("int total = a + b;", "total *= 2;");

		// A member cannot share its enclosing type's name (CS0542).
		string result = await subject.ExtractMethod(solutionPath, greeter, startLine, 1, endLine + 1, 1, methodName: "Greeter");

		await Assert.That(result).Contains("error=NotSupported");
		await Assert.That(result).Contains("CS0542");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(Content);
	}

	[Test]
	[Arguments("class")]
	[Arguments("1abc")]
	[Arguments("two words")]
	public async Task WhenTheMethodNameIsInvalid_ThenInvalidIsReturned(string methodName)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());

		string result = await subject.ExtractMethod(TestSolutions.Simple, "SimpleLibrary/Greeter.cs", 1, 1, 1, 2, methodName: methodName);

		await Assert.That(result).Contains("error=Invalid");
	}

	[Test]
	public async Task WhenTheSelectionIsOutsideTheDocument_ThenInvalidIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());

		string result = await subject.ExtractMethod(TestSolutions.Simple, "SimpleLibrary/Greeter.cs", 1, 1, 9999, 1);

		await Assert.That(result).Contains("error=Invalid");
	}

	[Test]
	public async Task WhenTheDocumentIsNotInTheSolution_ThenNotFoundIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());

		string result = await subject.ExtractMethod(TestSolutions.Simple, "Nope.cs", 1, 1, 1, 2);

		await Assert.That(result).Contains("error=NotFound");
	}

	private static (string SolutionPath, string Greeter) CreateScenario()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string greeter = Directory.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, "Greeter.cs", SearchOption.AllDirectories).First();
		File.WriteAllText(greeter, Content);
		return (solutionPath, greeter);
	}

	private static (int StartLine, int EndLine) Lines(string first, string last) =>
		(Position(first).Line, Position(last).Line);

	private static (int Line, int Column) Position(string fragment)
	{
		int offset = Content.IndexOf(fragment, StringComparison.Ordinal);
		string before = Content[..offset];
		int line = before.Count(character => character == '\n') + 1;
		int column = offset - (before.LastIndexOf('\n') + 1) + 1;
		return (line, column);
	}
}