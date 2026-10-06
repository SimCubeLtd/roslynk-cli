using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.CoreTests.Helpers;

namespace SimCube.Roslynk.CoreTests.Infrastructure.CodeActions;

public class CodeActionDiscoveryTests
{
	[Test]
	public async Task WhenTheCatalogIsBuilt_ThenItDiscoversCSharpProviders()
	{
		CodeActionCatalog catalog = CodeActionCatalog.Instance;

		await Assert.That(catalog.FixProviders.Count > 0).IsTrue().Because("Expected at least one fix provider.");
		await Assert.That(catalog.RefactoringProviders.Count > 0).IsTrue().Because("Expected at least one refactoring provider.");
	}

	[Test]
	public async Task WhenDiscoveringOnAnUnusedLocal_ThenARemoveFixIsFound()
	{
		string solutionPath = UnusedLocalScenario.Create(out string greeter, out int unusedLine);
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(solutionPath);
		var service = TestServices.CodeActions();

		Document document = CodeActionService.FindDocument(instance.CurrentSolution, greeter)!;
		SourceText text = await document.GetTextAsync();
		TextSpan span = CodeActionService.SpanFor(text, unusedLine, 1, unusedLine, 20);

		IReadOnlyList<DiscoveredAction> actions = await service.DiscoverAsync(document, span);

		await Assert.That(actions).Contains(action => action.DiagnosticId == "CS0219");
	}
}