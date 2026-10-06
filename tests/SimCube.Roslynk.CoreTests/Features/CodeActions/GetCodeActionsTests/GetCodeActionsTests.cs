using SimCube.Roslynk.Core.Features.CodeActions.GetCodeActions;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.CoreTests.Helpers;

namespace SimCube.Roslynk.CoreTests.Features.CodeActions.GetCodeActionsTests;

public class GetCodeActionsTests
{
	[Test]
	public async Task WhenAnAnalyzerDiagnosticIsAtThePosition_ThenItsActionIsListed()
	{
		string solutionPath = UnnecessaryUsingScenario.Create(out string greeter);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());

		string result = await subject.GetCodeActions(solutionPath, greeter, UnnecessaryUsingScenario.UsingLine, 1);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains(",IDE0005 ");
	}

	[Test]
	public async Task WhenAFixableDiagnosticIsAtThePosition_ThenItsActionIsListed()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());

		string result = await subject.GetCodeActions(solutionPath, greeter, unusedLine, 3, unusedLine, 20);

		await Assert.That(result).DoesNotContain("error=");
		// A body line is "<actionId>,<kind>,CS0219 <title>"; the actionId is the first field and is non-empty.
		string fixLine = result.Split('\n').First(line => line.Contains(",CS0219 ", StringComparison.Ordinal));
		await Assert.That(string.IsNullOrEmpty(fixLine.Split(',')[0])).IsFalse();
	}

	[Test]
	public async Task WhenTheDocumentIsUnknown_ThenNotFoundIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());

		string result = await subject.GetCodeActions(TestSolutions.Simple, "NoSuchFile.cs", 1, 1);

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());

		string result = await subject.GetCodeActions(TestSolutions.Simple, "Widget.cs", 1, 1);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}
}