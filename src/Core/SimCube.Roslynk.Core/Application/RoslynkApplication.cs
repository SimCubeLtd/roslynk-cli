using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Features.MultiQuery;
namespace SimCube.Roslynk.Core.Application;
/// <summary>Application entry point. Owns warm solution state; all Roslyn-bearing services are internal.</summary>
public sealed class RoslynkApplication
{
	private readonly object MaintenanceGate = new();
	private int ActiveOperations;
	private readonly IServiceProvider Provider;
	private readonly InstanceRegistry Registry;
	internal RoslynkApplication(IServiceProvider provider, InstanceRegistry registry)
	{
		Provider = provider;
		Registry = registry;
		_ = provider.GetRequiredService<Infrastructure.Observability.SolutionMetrics>();
	}
	private async Task PrepareAsync(string solutionPath, CancellationToken cancellationToken)
	{
		if (!File.Exists(solutionPath)) throw new FileNotFoundException($"No solution file at '{solutionPath}'.");
		RoslynInstance instance = await Registry.GetOrBeginAsync(solutionPath).WaitAsync(cancellationToken);
		await instance.WaitUntilReadyAsync().WaitAsync(cancellationToken);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		if (model.Status == SolutionStatus.Faulted) throw new InvalidOperationException(model.FaultMessage);
	}
	/// <summary>Evicts idle solutions only while the application is quiescent. Does not interrupt active reads or writes.</summary>
	public int EvictIdle(TimeSpan idleFor, DateTime nowUtc) => EvictIdle(idleFor, nowUtc, out _);

	/// <summary>
	/// As <see cref="EvictIdle(TimeSpan, DateTime)"/>. <paramref name="closedLastSolution"/> is true when this call
	/// evicted at least one solution and left none loaded, which is the host's signal that it is no longer needed.
	/// An application that never loaded a solution does not report it.
	/// </summary>
	public int EvictIdle(TimeSpan idleFor, DateTime nowUtc, out bool closedLastSolution)
	{
		lock (MaintenanceGate)
		{
			int evicted = ActiveOperations == 0 ? Registry.EvictIdle(idleFor, nowUtc) : 0;
			closedLastSolution = evicted > 0 && Registry.LoadedInstances().Count == 0;
			return evicted;
		}
	}
	private async Task<OperationResult> ExecuteAsync(Func<Task<string>> action)
	{
		lock (MaintenanceGate) ActiveOperations++;
		try { return OperationResult.FromOutline(await action()); }
		catch (OperationCanceledException) { throw; }
		catch (Exception exception) { return OperationResult.FromOutline(OutlineError.Format(OperationErrors.ToError(exception), SolutionStatus.Ready)); }
		finally { lock (MaintenanceGate) ActiveOperations--; }
	}

	public Task<OperationResult> GetCallersAsync(GetCallersRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Callers.GetCallers.GetCallersTool>().GetCallers(request.SolutionPath, request.MethodName, cancellationToken);
	});

	public Task<OperationResult> ApplyCodeActionAsync(ApplyCodeActionRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction.ApplyCodeActionTool>().ApplyCodeAction(request.SolutionPath, request.ActionId, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeFix.ApplyCodeFixTool>().ApplyCodeFix(request.SolutionPath, request.DocumentPath, request.DiagnosticId, request.Line, request.Column, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> GetCodeActionsAsync(GetCodeActionsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.CodeActions.GetCodeActions.GetCodeActionsTool>().GetCodeActions(request.SolutionPath, request.DocumentPath, request.Line, request.Column, request.EndLine, request.EndColumn, cancellationToken);
	});

	public Task<OperationResult> FindDeadConditionalsAsync(FindDeadConditionalsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Conditionals.FindDeadConditionals.FindDeadConditionalsTool>().FindDeadConditionals(request.SolutionPath, cancellationToken);
	});

	public Task<OperationResult> FindDeadCodeAsync(FindDeadCodeRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.DeadCode.FindDeadCode.FindDeadCodeTool>().FindDeadCode(request.SolutionPath, request.Scope, request.IncludePublic, request.MaxResults, cancellationToken);
	});

	public Task<OperationResult> GetDiagnosticsAsync(GetDiagnosticsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics.GetDiagnosticsTool>().GetDiagnostics(request.SolutionPath, request.IncludeErrors, request.IncludeWarnings, request.IncludeInfo, request.IncludeHidden, request.IncludeAnalyzers, cancellationToken);
	});

	public Task<OperationResult> MultiQueryAsync(MultiQueryRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.MultiQuery.MultiQueryTool>().MultiQuery(request.SolutionPath, request.Operations.Select(ToLegacyQuery).ToArray(), request.ExpectSnapshot, cancellationToken);
	});

	public Task<OperationResult> ApplyPatchAsync(ApplyPatchRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Patching.ApplyPatch.ApplyPatchTool>().ApplyPatch(request.SolutionPath, request.Patch, request.BaseVersions?.Select(version => new Features.Patching.ApplyPatch.FileVersion(version.Path, version.Version)).ToArray(), request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> ExtractMethodAsync(ExtractMethodRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Refactorings.ExtractMethod.ExtractMethodTool>().ExtractMethod(request.SolutionPath, request.DocumentPath, request.StartLine, request.StartColumn, request.EndLine, request.EndColumn, request.MethodName, request.AsLocalFunction, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> FindReadsAsync(FindReadsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.References.FindReads.FindReadsTool>().FindReads(request.SolutionPath, request.SymbolName, request.MaxResults, cancellationToken);
	});

	public Task<OperationResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.References.FindReferences.FindReferencesTool>().FindReferences(request.SolutionPath, request.SymbolName, request.MaxResults, cancellationToken);
	});

	public Task<OperationResult> FindWritesAsync(FindWritesRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.References.FindWrites.FindWritesTool>().FindWrites(request.SolutionPath, request.SymbolName, request.MaxResults, cancellationToken);
	});

	public Task<OperationResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.References.RenameSymbol.RenameSymbolTool>().RenameSymbol(request.SolutionPath, request.SymbolName, request.NewName, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> ChangeSignatureAsync(ChangeSignatureRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Signatures.ChangeSignature.ChangeSignatureTool>().ChangeSignature(request.SolutionPath, request.MethodId, request.ParameterType, request.ParameterName, request.DefaultValue, request.CallSiteArgument, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> RenameParameterAsync(RenameParameterRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Signatures.RenameParameter.RenameParameterTool>().RenameParameter(request.SolutionPath, request.MethodId, request.ParameterName, request.NewName, request.CheckOnly, cancellationToken);
	});

	public Task<OperationResult> GetSolutionStatusAsync(GetSolutionStatusRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Solutions.GetSolutionStatus.GetSolutionStatusTool>().GetSolutionStatus());
	});

	public Task<OperationResult> OpenSolutionAsync(OpenSolutionRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Solutions.OpenSolution.OpenSolutionTool>().OpenSolution(request.SolutionPath));
	});

	public Task<OperationResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
	{
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Solutions.ReloadSolution.ReloadSolutionTool>().ReloadSolution(request.SolutionPath));
	});

	public Task<OperationResult> FindDefinitionAsync(FindDefinitionRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.FindDefinition.FindDefinitionTool>().FindDefinition(request.SolutionPath, request.FilePath, request.Line, request.Column, cancellationToken);
	});

	public Task<OperationResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.FindImplementations.FindImplementationsTool>().FindImplementations(request.SolutionPath, request.SymbolName, cancellationToken);
	});

	public Task<OperationResult> GetExpressionInfoAsync(GetExpressionInfoRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.GetExpressionInfo.GetExpressionInfoTool>().GetExpressionInfo(request.SolutionPath, request.FilePath, request.Line, request.Column, cancellationToken);
	});

	public Task<OperationResult> GetMembersAsync(GetMembersRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.GetMembers.GetMembersTool>().GetMembers(request.SolutionPath, request.TypeName, request.IncludeInherited, request.NameFilter, request.IncludeMethods, request.IncludeFields, request.IncludeProperties, request.IncludeEvents, request.IncludeNestedTypes, cancellationToken);
	});

	public Task<OperationResult> GetSymbolAsync(GetSymbolRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.GetSymbol.GetSymbolTool>().GetSymbol(request.SolutionPath, request.SymbolName, cancellationToken);
	});

	public Task<OperationResult> GetSymbolBodyAsync(GetSymbolBodyRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody.GetSymbolBodyTool>().GetSymbolBody(request.SolutionPath, request.SymbolName, request.IncludeLeadingTrivia, cancellationToken);
	});

	public Task<OperationResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy.GetTypeHierarchyTool>().GetTypeHierarchy(request.SolutionPath, request.TypeName, cancellationToken);
	});

	public Task<OperationResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Symbols.SearchSymbols.SearchSymbolsTool>().SearchSymbols(request.SolutionPath, request.Query, request.MaxResults, cancellationToken);
	});

	public Task<OperationResult> RemoveUnusedUsingsAsync(RemoveUnusedUsingsRequest request, CancellationToken cancellationToken = default) => ExecuteAsync(async () =>
	{
		await PrepareAsync(request.SolutionPath, cancellationToken);
		return await Provider.GetRequiredService<SimCube.Roslynk.Core.Features.Usings.RemoveUnusedUsings.RemoveUnusedUsingsTool>().RemoveUnusedUsings(request.SolutionPath, request.DocumentPath, request.CheckOnly, cancellationToken);
	});

	private static MultiQueryOperation ToLegacyQuery(QueryOperation operation) => operation switch {
		GetCallersQuery query => new(MultiQueryOp.get_callers, new Dictionary<string, JsonElement> { ["methodName"] = JsonSerializer.SerializeToElement(query.Request.MethodName) }),
		FindDeadConditionalsQuery query => new(MultiQueryOp.find_dead_conditionals, new Dictionary<string, JsonElement> { }),
		FindDeadCodeQuery query => new(MultiQueryOp.find_dead_code, new Dictionary<string, JsonElement> { ["scope"] = JsonSerializer.SerializeToElement(query.Request.Scope), ["includePublic"] = JsonSerializer.SerializeToElement(query.Request.IncludePublic), ["maxResults"] = JsonSerializer.SerializeToElement(query.Request.MaxResults) }),
		FindReadsQuery query => new(MultiQueryOp.find_reads, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName), ["maxResults"] = JsonSerializer.SerializeToElement(query.Request.MaxResults) }),
		FindReferencesQuery query => new(MultiQueryOp.find_references, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName), ["maxResults"] = JsonSerializer.SerializeToElement(query.Request.MaxResults) }),
		FindWritesQuery query => new(MultiQueryOp.find_writes, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName), ["maxResults"] = JsonSerializer.SerializeToElement(query.Request.MaxResults) }),
		FindDefinitionQuery query => new(MultiQueryOp.find_definition, new Dictionary<string, JsonElement> { ["filePath"] = JsonSerializer.SerializeToElement(query.Request.FilePath), ["line"] = JsonSerializer.SerializeToElement(query.Request.Line), ["column"] = JsonSerializer.SerializeToElement(query.Request.Column) }),
		FindImplementationsQuery query => new(MultiQueryOp.find_implementations, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName) }),
		GetExpressionInfoQuery query => new(MultiQueryOp.get_expression_info, new Dictionary<string, JsonElement> { ["filePath"] = JsonSerializer.SerializeToElement(query.Request.FilePath), ["line"] = JsonSerializer.SerializeToElement(query.Request.Line), ["column"] = JsonSerializer.SerializeToElement(query.Request.Column) }),
		GetMembersQuery query => new(MultiQueryOp.get_members, new Dictionary<string, JsonElement> { ["typeName"] = JsonSerializer.SerializeToElement(query.Request.TypeName), ["includeInherited"] = JsonSerializer.SerializeToElement(query.Request.IncludeInherited), ["nameFilter"] = JsonSerializer.SerializeToElement(query.Request.NameFilter), ["includeMethods"] = JsonSerializer.SerializeToElement(query.Request.IncludeMethods), ["includeFields"] = JsonSerializer.SerializeToElement(query.Request.IncludeFields), ["includeProperties"] = JsonSerializer.SerializeToElement(query.Request.IncludeProperties), ["includeEvents"] = JsonSerializer.SerializeToElement(query.Request.IncludeEvents), ["includeNestedTypes"] = JsonSerializer.SerializeToElement(query.Request.IncludeNestedTypes) }),
		GetSymbolQuery query => new(MultiQueryOp.get_symbol, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName) }),
		GetSymbolBodyQuery query => new(MultiQueryOp.get_symbol_body, new Dictionary<string, JsonElement> { ["symbolName"] = JsonSerializer.SerializeToElement(query.Request.SymbolName), ["includeLeadingTrivia"] = JsonSerializer.SerializeToElement(query.Request.IncludeLeadingTrivia) }),
		GetTypeHierarchyQuery query => new(MultiQueryOp.get_type_hierarchy, new Dictionary<string, JsonElement> { ["typeName"] = JsonSerializer.SerializeToElement(query.Request.TypeName) }),
		SearchSymbolsQuery query => new(MultiQueryOp.search_symbols, new Dictionary<string, JsonElement> { ["query"] = JsonSerializer.SerializeToElement(query.Request.Query), ["maxResults"] = JsonSerializer.SerializeToElement(query.Request.MaxResults) }),
		_ => throw new ArgumentException("Unsupported batch query.")
	};
}