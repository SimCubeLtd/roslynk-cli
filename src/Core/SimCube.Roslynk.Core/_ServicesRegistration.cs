using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Observability;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.Core;

public static class ServicesRegistration
{
	/// <summary>
	/// Registers Roslynk's engine and feature services. Registrations are added here as each
	/// Infrastructure area and feature slice is built.
	/// </summary>
	public static IServiceCollection AddRoslynk(this IServiceCollection services)
	{
		services.AddSingleton<InstanceRegistry>();
		services.AddSingleton<DiagnosticsService>();
		services.AddSingleton<SymbolResolver>();
		services.AddSingleton<ProjectionService>();
		services.AddSingleton<ConditionalCoverage>();
		services.AddSingleton<ApplyPipeline>();
		services.AddSingleton<DocumentDiagnosticsProvider>();
		services.AddSingleton<CodeActionService>();
		services.AddSingleton(provider => new SolutionMetrics(RoslynkMeter.Instance, provider.GetRequiredService<InstanceRegistry>()));
		services.AddSingleton(provider => new Application.RoslynkApplication(provider, provider.GetRequiredService<InstanceRegistry>()));
		services.AddSingleton<SimCube.Roslynk.Core.Features.Callers.GetCallers.GetCallersTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction.ApplyCodeActionTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeFix.ApplyCodeFixTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.CodeActions.GetCodeActions.GetCodeActionsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Conditionals.FindDeadConditionals.FindDeadConditionalsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.DeadCode.FindDeadCode.FindDeadCodeTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics.GetDiagnosticsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.MultiQuery.MultiQueryTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Patching.ApplyPatch.ApplyPatchTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Refactorings.ExtractMethod.ExtractMethodTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.References.FindReads.FindReadsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.References.FindReferences.FindReferencesTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.References.FindWrites.FindWritesTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.References.RenameSymbol.RenameSymbolTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Signatures.ChangeSignature.ChangeSignatureTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Signatures.RenameParameter.RenameParameterTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Solutions.GetSolutionStatus.GetSolutionStatusTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Solutions.OpenSolution.OpenSolutionTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Solutions.ReloadSolution.ReloadSolutionTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.FindDefinition.FindDefinitionTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.FindImplementations.FindImplementationsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.GetExpressionInfo.GetExpressionInfoTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.GetMembers.GetMembersTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.GetSymbol.GetSymbolTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody.GetSymbolBodyTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy.GetTypeHierarchyTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Symbols.SearchSymbols.SearchSymbolsTool>();
		services.AddSingleton<SimCube.Roslynk.Core.Features.Usings.RemoveUnusedUsings.RemoveUnusedUsingsTool>();
		return services;
	}
}
