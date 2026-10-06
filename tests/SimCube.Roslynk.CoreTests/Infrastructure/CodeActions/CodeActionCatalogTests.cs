using SimCube.Roslynk.Core.Infrastructure.CodeActions;

namespace SimCube.Roslynk.CoreTests.Infrastructure.CodeActions;

public class CodeActionCatalogTests
{
	[Test]
	public async Task WhenTheCatalogIsBuilt_ThenCompilerIdsAreFixable()
	{
		// The catalog is built by reflection over the Features assemblies, so a packaging change could
		// silently empty it and leave every diagnostic unfixable.
		await Assert.That(CodeActionCatalog.Instance.FixableDiagnosticIds).Contains("CS0219");
	}

	[Test]
	public async Task WhenTheCatalogIsBuilt_ThenTheUnnecessaryImportsTriggerIsFixable()
	{
		// Roslyn's fixer registers against this private trigger rather than IDE0005; the analyzer must be
		// kept by the narrowing pass on the strength of it.
		await Assert.That(CodeActionCatalog.Instance.FixableDiagnosticIds).Contains("RemoveUnnecessaryImportsFixable");
	}

	[Test]
	public async Task WhenAFixIsTriggeredByAPrivateId_ThenItIsReportedUnderThePublicId()
	{
		await Assert.That(CodeActionCatalog.PublicId("RemoveUnnecessaryImportsFixable")).IsEqualTo("IDE0005");
	}

	[Test]
	public async Task WhenAFixIsTriggeredByItsOwnId_ThenThatIdIsReported()
	{
		await Assert.That(CodeActionCatalog.PublicId("CS0219")).IsEqualTo("CS0219");
	}
}