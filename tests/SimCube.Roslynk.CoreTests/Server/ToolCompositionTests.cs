using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Application;
using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeFix;
using SimCube.Roslynk.Core.Features.Usings.RemoveUnusedUsings;

namespace SimCube.Roslynk.CoreTests.Server;

public class ToolCompositionTests
{
	[Test]
	public async Task WhenTheServerIsComposedAsTheHostDoes_ThenTheToolsResolve()
	{
		// The tools that drive fixes take the analyzer-aware diagnostics provider, so a missing registration
		// would only show up when a tool is first invoked.
		using ServiceProvider provider = Build();

		await Assert.That(provider.GetRequiredService<ApplyCodeFixTool>()).IsNotNull();
		await Assert.That(provider.GetRequiredService<RemoveUnusedUsingsTool>()).IsNotNull();
	}

	[Test]
	public async Task WhenTheServerIsComposedAsTheHostDoes_ThenTheToolSurfaceIsExposed()
	{
		using ServiceProvider provider = Build();

		await Assert.That(provider.GetRequiredService<RoslynkApplication>()).IsNotNull();
		await Assert.That(provider.GetRequiredService<ApplyCodeFixTool>()).IsNotNull();
		await Assert.That(provider.GetRequiredService<RemoveUnusedUsingsTool>()).IsNotNull();
	}

	private static ServiceProvider Build()
	{
		var services = new ServiceCollection();
		services.AddRoslynk();
		services.AddSingleton<ApplyCodeFixTool>();
		services.AddSingleton<RemoveUnusedUsingsTool>();
		return services.BuildServiceProvider();
	}
}