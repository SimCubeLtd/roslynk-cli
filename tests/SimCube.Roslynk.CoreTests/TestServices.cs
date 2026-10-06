using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeFix;
using SimCube.Roslynk.Core.Features.Usings.RemoveUnusedUsings;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests;

/// <summary>
/// Composes the code-action services the way <c>AddRoslynk</c> does, so tests share one
/// <see cref="DocumentDiagnosticsProvider"/> between a tool and the service it calls, as the server does.
/// </summary>
internal static class TestServices
{
	public static CodeActionService CodeActions() => CodeActions(new DocumentDiagnosticsProvider());

	public static CodeActionService CodeActions(DocumentDiagnosticsProvider documentDiagnostics) => new(documentDiagnostics);

	public static ApplyCodeFixTool ApplyCodeFix(InstanceRegistry registry)
	{
		var documentDiagnostics = new DocumentDiagnosticsProvider();
		return new ApplyCodeFixTool(registry, CodeActions(documentDiagnostics), new ApplyPipeline(), documentDiagnostics);
	}

	public static RemoveUnusedUsingsTool RemoveUnusedUsings(InstanceRegistry registry)
	{
		var documentDiagnostics = new DocumentDiagnosticsProvider();
		return new RemoveUnusedUsingsTool(registry, new ApplyPipeline(), CodeActions(documentDiagnostics), documentDiagnostics);
	}
}
