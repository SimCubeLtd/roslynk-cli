using System.IO;
using SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.CoreTests.Features.Diagnostics.GeneratorDiagnosticsTests;

public class GeneratorDiagnosticsTests
{
	[Test]
	public async Task WhenAProjectReferencesASourceGenerator_ThenGeneratedTypesAreInTheCompilation()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Generator);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(TestSolutions.Generator);

		// The consumer compiles only if HelloGenerator's output is part of the compilation.
		await Assert.That(result).Contains("errors=0");
		await Assert.That(result).DoesNotContain("CS0246");
	}

	[Test]
	public async Task WhenTheGeneratorAssemblyCannotBeLoaded_ThenTheLoadReportsTheFailure()
	{
		// A scratch copy excludes bin/obj; plant a corrupt DLL at the generator's output path so the
		// analyzer reference resolves to a file that cannot possibly load.
		string solutionPath = TestSolutions.CreateScratchGeneratorSolutionWithoutBuiltGenerator();
		string corruptDll = Path.Combine(
			Path.GetDirectoryName(solutionPath)!, "GeneratorLib", "bin", "Debug", "netstandard2.0", "GeneratorLib.dll");
		Directory.CreateDirectory(Path.GetDirectoryName(corruptDll)!);
		File.WriteAllText(corruptDll, "not a PE image");

		using SolutionWorkspace workspace = await SolutionWorkspace.LoadAsync(solutionPath);

		// The generator cannot run, so the unloadable reference must be called out rather than the
		// compilation silently degrading to phantom CS0246s.
		await Assert.That(workspace.LoadDiagnostics).Contains(d =>
			d.Contains("Analyzer load failed", StringComparison.Ordinal) &&
			d.Contains("GeneratorLib.dll", StringComparison.OrdinalIgnoreCase));
	}
}