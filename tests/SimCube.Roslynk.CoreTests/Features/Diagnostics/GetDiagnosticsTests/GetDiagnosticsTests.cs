using SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.Diagnostics.GetDiagnosticsTests;

public class GetDiagnosticsTests
{
	[Test]
	public async Task WhenASolutionHasACompileError_ThenItIsReturnedAsAnError()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Broken);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(TestSolutions.Broken, includeErrors: true);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).DoesNotContain("errors=0");
		await Assert.That(result.Split('\n')).Contains(line => line == "BrokenLibrary");
		await Assert.That(result).Contains("\terrors\n");
		await Assert.That(result).Contains("CS0029,9:27,");
	}

	[Test]
	public async Task WhenSeveritiesAreNotWidened_ThenOnlyErrorsAreReturnedButTheCountsStillSeeWarnings()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Broken);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(TestSolutions.Broken, includeErrors: true);

		// Counts are always in the header; body only shows errors when includeErrors is set.
		await Assert.That(result).Contains("warnings=0");
		await Assert.That(result).DoesNotContain("\twarnings");
	}

	[Test]
	public async Task WhenWarningsAreIncluded_ThenTheyAppearAlongsideErrors()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Broken);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(
			TestSolutions.Broken, includeErrors: true, includeWarnings: true);

		// The fixture has zero warnings, so only errors appear in the body.
		await Assert.That(result).Contains("warnings=0");
		await Assert.That(result).Contains("\terrors\n");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(TestSolutions.Broken);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Broken);
	}
}