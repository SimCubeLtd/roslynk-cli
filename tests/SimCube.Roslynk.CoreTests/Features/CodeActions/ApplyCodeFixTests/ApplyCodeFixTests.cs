using System.IO;
using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction;
using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeFix;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Writing;
using SimCube.Roslynk.CoreTests.Helpers;

namespace SimCube.Roslynk.CoreTests.Features.CodeActions.ApplyCodeFixTests;

public class ApplyCodeFixTests
{
	/// <summary>The 1-based column of <c>unused</c> in UnusedLocalScenario's <c>int unused = 0;</c> line (indented by two tabs).</summary>
	private const int UnusedColumn = 7;

	[Test]
	public async Task WhenFixingAnAnalyzerDiagnosticById_ThenTheFileIsUpdated()
	{
		// The point of issue #9: IDE0005 is listed by get_diagnostics, so it must be fixable here too.
		string solutionPath = UnnecessaryUsingScenario.Create(out string greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "IDE0005", UnnecessaryUsingScenario.UsingLine, 1);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await File.ReadAllTextAsync(greeter)).DoesNotContain(UnnecessaryUsingScenario.UnnecessaryUsing);
	}

	[Test]
	public async Task WhenFixingAnAnalyzerDiagnosticCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = UnnecessaryUsingScenario.Create(out string greeter);
		string before = await File.ReadAllTextAsync(greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "IDE0005", UnnecessaryUsingScenario.UsingLine, 1, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result).Contains("Greeter.cs");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenFixingADiagnosticById_ThenTheFileIsUpdated()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0219", unusedLine, UnusedColumn);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await File.ReadAllTextAsync(greeter)).DoesNotContain("int unused");
	}

	[Test]
	public async Task WhenNoSuchDiagnosticExists_ThenNotFoundIsReturned()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS9999", unusedLine, UnusedColumn);

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(TestSolutions.Simple, "Widget.cs", "CS0219", 1, 1);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	[Test]
	public async Task WhenTheFileHasSeveralOccurrences_ThenOnlyTheOneAtThePositionIsFixed()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		string content = (await File.ReadAllTextAsync(greeter)).Replace("\t\tint unused = 0;\r\n", "\t\tint unused = 0;\r\n\t\tint spare = 0;\r\n");
		await File.WriteAllTextAsync(greeter, content);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0219", unusedLine + 1, UnusedColumn);

		await Assert.That(result.Contains("applied=Y")).IsTrue();
		string after = await File.ReadAllTextAsync(greeter);
		await Assert.That(after).Contains("int unused = 0;");
		await Assert.That(after).DoesNotContain("int spare");
	}

	[Test]
	public async Task WhenThePositionIsInsideTheDiagnosticSpan_ThenItIsFixed()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0219", unusedLine, UnusedColumn + "unused".Length);

		await Assert.That(result.Contains("applied=Y")).IsTrue();
		await Assert.That(await File.ReadAllTextAsync(greeter)).DoesNotContain("int unused");
	}

	[Test]
	public async Task WhenNoDiagnosticIsAtThePosition_ThenNotFoundIsReturnedAndNothingIsWritten()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		string before = await File.ReadAllTextAsync(greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0219", unusedLine - 2, 1);

		await Assert.That(result).Contains("error=NotFound");
		await Assert.That(result).Contains($"{unusedLine - 2}:1");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(before);
	}

	[Test]
	[Arguments(0, 1)]
	[Arguments(1, 0)]
	public async Task WhenThePositionIsNotOneBased_ThenInvalidIsReturned(int line, int column)
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out _);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0219", line, column);

		await Assert.That(result).Contains("error=Invalid");
	}

	[Test]
	public async Task WhenTheDiagnosticHasSeveralFixes_ThenConflictListsEachOnceAndNothingIsWritten()
	{
		string solutionPath = CreateUnimplementedInterface(out string greeter, out int line, out int column);
		string before = await File.ReadAllTextAsync(greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0535", line, column);

		await Assert.That(result).Contains("error=Conflict");
		await Assert.That(result).Contains("apply_code_action");
		string[] candidates = Candidates(result);
		// Each member missing from the interface reports its own CS0535 offering the same two fixes.
		await Assert.That(candidates.Length).IsEqualTo(2);
		await Assert.That(candidates).Contains(candidate => candidate.EndsWith(",Fix,CS0535 Implement interface", StringComparison.Ordinal));
		await Assert.That(candidates).Contains(candidate => candidate.EndsWith(",Fix,CS0535 Implement all members explicitly", StringComparison.Ordinal));
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenACandidateIsPassedToApplyCodeAction_ThenThatFixIsApplied()
	{
		string solutionPath = CreateUnimplementedInterface(out string greeter, out int line, out int column);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		CodeActionService service = TestServices.CodeActions();
		string conflict = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, greeter, "CS0535", line, column);
		string candidate = Candidates(conflict).Single(entry => entry.EndsWith(" Implement all members explicitly", StringComparison.Ordinal));
		string actionId = candidate[..candidate.IndexOf(',')];

		string result = await new ApplyCodeActionTool(registry, service, new ApplyPipeline()).ApplyCodeAction(solutionPath, actionId);

		await Assert.That(result.Contains("applied=Y")).IsTrue();
		await Assert.That(await File.ReadAllTextAsync(greeter)).Contains("void IThing.Run()");
	}

	[Test]
	public async Task WhenAFixIsGroupedUnderAParentAction_ThenTheNestedFixesAreCandidates()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		string content = (await File.ReadAllTextAsync(greeter)).Replace("\t\tint unused = 0;", "\t\tStringBuilder unused = null;");
		await File.WriteAllTextAsync(greeter, content);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = TestServices.ApplyCodeFix(registry);

		string result = await subject.ApplyCodeFix(solutionPath, greeter, "CS0246", unusedLine, 3);

		await Assert.That(result).Contains("error=Conflict");
		string[] candidates = Candidates(result);
		await Assert.That(candidates).Contains(candidate => candidate.EndsWith(",Fix,CS0246 using System.Text;", StringComparison.Ordinal));
		await Assert.That(candidates).Contains(candidate => candidate.EndsWith(",Fix,CS0246 Generate class 'StringBuilder'", StringComparison.Ordinal));
	}

	private static string[] Candidates(string result) =>
		result.Split('\n')
			.Where(entry => entry.StartsWith("candidate=", StringComparison.Ordinal))
			.Select(entry => entry["candidate=".Length..].TrimEnd('\r'))
			.ToArray();

	/// <summary>A scratch SimpleSolution whose Greeter.cs declares a class that implements none of its interface's members (CS0535).</summary>
	private static string CreateUnimplementedInterface(out string greeterPath, out int line, out int column)
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		greeterPath = Directory.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, "Greeter.cs", SearchOption.AllDirectories).First();

		const string content =
			"namespace SimpleLibrary;\r\n\r\n" +
			"public interface IThing\r\n{\r\n\tvoid Run();\r\n\tint Count { get; }\r\n}\r\n\r\n" +
			"public class Thing : IThing\r\n{\r\n}\r\n\r\n" +
			"public class Greeter : IGreeter\r\n{\r\n" +
			"\tpublic string Greet(string name) => $\"Hello, {name}!\";\r\n}\r\n";
		File.WriteAllText(greeterPath, content);

		int offset = content.IndexOf(": IThing", StringComparison.Ordinal) + 2;
		line = content[..offset].Count(character => character == '\n') + 1;
		column = offset - content.LastIndexOf('\n', offset - 1);
		return solutionPath;
	}
}