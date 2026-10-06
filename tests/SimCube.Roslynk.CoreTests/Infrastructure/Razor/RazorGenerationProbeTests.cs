using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Razor;

public class RazorGenerationProbeTests
{
	/// <summary>
	/// The SDK's Razor source generator targets a newer Roslyn than we load, so the workspace's analyzer
	/// loader refuses it and produces no documents. <see cref="SimCube.Roslynk.Core.Infrastructure.Razor.RazorDocumentGenerator"/>
	/// works around that by running the generator itself and adding the result as a document, so the component
	/// partial enters the compilation. These tests guard that workaround.
	/// </summary>
	[Test]
	public async Task WhenARazorProjectIsLoaded_ThenTheGeneratedComponentDocumentIsAdded()
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Razor);
		Project project = instance.CurrentSolution.Projects.First();

		await Assert.That(project.Documents).Contains(document =>
			document.Name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
			&& document.Name.Contains("Counter", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public async Task WhenARazorProjectIsLoaded_ThenTheComponentPartialBaseIsInTheCompilation()
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.Razor);
		Project project = instance.CurrentSolution.Projects.First();

		Compilation compilation = (await project.GetCompilationAsync())!;
		INamedTypeSymbol? counter = compilation.GetTypeByMetadataName("RazorLib.Counter");

		await Assert.That(counter).IsNotNull();
		await Assert.That(counter!.BaseType?.Name).IsEqualTo("ComponentBase");
	}
}