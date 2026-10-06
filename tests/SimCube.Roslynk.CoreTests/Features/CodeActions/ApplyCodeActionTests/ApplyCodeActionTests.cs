using System.IO;
using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction;
using SimCube.Roslynk.Core.Features.CodeActions.GetCodeActions;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Writing;
using SimCube.Roslynk.CoreTests.Helpers;

namespace SimCube.Roslynk.CoreTests.Features.CodeActions.ApplyCodeActionTests;

public class ApplyCodeActionTests
{
	[Test]
	public async Task WhenApplyingADiscoveredFix_ThenTheFileIsUpdated()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var service = TestServices.CodeActions();
		string actionId = await DiscoverRemoveUnusedAsync(registry, service, solutionPath, greeter, unusedLine);
		var subject = new ApplyCodeActionTool(registry, service, new ApplyPipeline());

		string result = await subject.ApplyCodeAction(solutionPath, actionId);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await File.ReadAllTextAsync(greeter)).DoesNotContain("int unused");
	}

	[Test]
	public async Task WhenApplyingWithCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var service = TestServices.CodeActions();
		string before = await File.ReadAllTextAsync(greeter);
		string actionId = await DiscoverRemoveUnusedAsync(registry, service, solutionPath, greeter, unusedLine);
		var subject = new ApplyCodeActionTool(registry, service, new ApplyPipeline());

		string result = await subject.ApplyCodeAction(solutionPath, actionId, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenTheActionIdIsInvalid_ThenItIsRefused()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new ApplyCodeActionTool(registry, TestServices.CodeActions(), new ApplyPipeline());

		string result = await subject.ApplyCodeAction(TestSolutions.Simple, "not-a-valid-id");

		await Assert.That(result).Contains("error=Invalid");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new ApplyCodeActionTool(registry, TestServices.CodeActions(), new ApplyPipeline());

		string result = await subject.ApplyCodeAction(TestSolutions.Simple, "anything");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static async Task<string> DiscoverRemoveUnusedAsync(InstanceRegistry registry, CodeActionService service, string solutionPath, string greeter, int unusedLine)
	{
		var getActions = new GetCodeActionsTool(registry, service);
		string actions = await getActions.GetCodeActions(solutionPath, greeter, unusedLine, 3, unusedLine, 20);

		// A body line is "<actionId>,<kind>,CS0219 <title>"; take the first field of the CS0219 line.
		string fixLine = actions.Split('\n').First(line => line.Contains(",CS0219 ", StringComparison.Ordinal));
		return fixLine.Split(',')[0];
	}
}