using System.IO;
using SimCube.Roslynk.Core.Features.Signatures.ChangeSignature;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.Signatures.ChangeSignatureTests;

public class ChangeSignatureTests
{
	[Test]
	public async Task WhenAddingAParameterWithACallSiteValue_ThenTheMethodAndItsCallsAreUpdated()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			solutionPath, "SimpleLibrary.Widget.Compute", "int", "factor", "1", callSiteArgument: "1");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("updatedCallSites=1");
		string text = await File.ReadAllTextAsync(FindFile(solutionPath, "Widget.cs"));
		await Assert.That(text).Contains("int factor = 1");
		await Assert.That(text).Contains("factor: 1");
	}

	[Test]
	public async Task WhenAddingAParameterWithCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());
		string widget = FindFile(solutionPath, "Widget.cs");
		string before = await File.ReadAllTextAsync(widget);

		string result = await subject.ChangeSignature(
			solutionPath, "SimpleLibrary.Widget.Compute", "int", "factor", "1", callSiteArgument: "1", checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result).Contains("Widget.cs");
		await Assert.That(await File.ReadAllTextAsync(widget)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenTheMethodImplementsAnInterfaceMember_ThenItIsNotSupported()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			TestSolutions.Simple, "SimpleLibrary.Greeter.Greet", "int", "times", "1");

		await Assert.That(result).Contains("error=NotSupported");
		await Assert.That(result).Contains("interface");
	}

	[Test]
	public async Task WhenNoDefaultValueIsGiven_ThenItIsRefused()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			TestSolutions.Simple, "SimpleLibrary.Widget.Compute", "int", "factor", "");

		await Assert.That(result).Contains("error=Invalid");
		await Assert.That(result).Contains("default");
	}

	[Test]
	public async Task WhenTheMethodIsNotFound_ThenNotFoundIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			TestSolutions.Simple, "SimpleLibrary.DoesNotExist", "int", "factor", "1");

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			TestSolutions.Simple, "SimpleLibrary.Widget.Compute", "int", "factor", "1");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	[Test]
	public async Task WhenAnOverloadIsTargetedBySignature_ThenThatOverloadIsChanged()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			solutionPath, "SimpleLibrary.Ledger.Add(int, int)", "int", "step", "1", callSiteArgument: "1");

		await Assert.That(result).Contains("applied=Y");
		string text = await File.ReadAllTextAsync(FindFile(solutionPath, "Ledger.cs"));
		await Assert.That(text).Contains("public int Add(int amount, int times, int step = 1)");
		await Assert.That(text).Contains("public int Add(int amount)");
	}

	[Test]
	public async Task WhenTheMethodIsDeclaredInARazorCodeBlock_ThenTheRazorFileIsRewrittenOnDisk()
	{
		string solutionPath = await CreateRazorSolutionWithLabelAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(
			solutionPath, "RazorLib.Counter.Label", "string", "prefix", "\"#\"", callSiteArgument: "\"x\"");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("updatedCallSites=2");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");
		string counter = await File.ReadAllTextAsync(FindFile(solutionPath, "Counter.razor"));
		await Assert.That(counter).Contains("private string Label(int value, string prefix = \"#\")");
		await Assert.That(counter).Contains("@Label(1, prefix: \"x\")");
		await Assert.That(counter).Contains("Label(2, prefix: \"x\");");
	}

	[Test]
	public async Task WhenTheRazorMethodIsChangedWithCheckOnly_ThenTheRazorFileIsListedAndNothingIsWritten()
	{
		string solutionPath = await CreateRazorSolutionWithLabelAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());
		string counterPath = FindFile(solutionPath, "Counter.razor");
		string before = await File.ReadAllTextAsync(counterPath);

		string result = await subject.ChangeSignature(
			solutionPath, "RazorLib.Counter.Label", "string", "prefix", "\"#\"", checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result.Split('\n')).Contains(line => line.TrimStart('\t') == "Counter.razor");
		await Assert.That(await File.ReadAllTextAsync(counterPath)).IsEqualTo(before);
	}

	/// <summary>A scratch Razor solution whose Counter declares Label, called from markup and from the @code block.</summary>
	private static async Task<string> CreateRazorSolutionWithLabelAsync()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string counterPath = FindFile(solutionPath, "Counter.razor");
		string counter = await File.ReadAllTextAsync(counterPath);
		counter = counter
			.Replace("<p>Starting from @StartAt</p>", "<p>Starting from @StartAt</p>\r\n<p>@Label(1)</p>")
			.Replace("\tprivate void IncrementCount()", "\tprivate string Label(int value) => $\"#{value}\";\r\n\r\n\tprivate void Reset()\r\n\t{\r\n\t\tLabel(2);\r\n\t}\r\n\r\n\tprivate void IncrementCount()");
		await File.WriteAllTextAsync(counterPath, counter);
		return solutionPath;
	}

	private static string FindFile(string solutionPath, string fileName) =>
		Directory.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, fileName, SearchOption.AllDirectories).First();
}