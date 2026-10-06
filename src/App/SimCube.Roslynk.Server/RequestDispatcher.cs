using SimCube.Roslynk.Core.Application;
using SimCube.Roslynk.Protocol;
using Domain = SimCube.Roslynk.Core.Application;
using Transport = SimCube.Roslynk.Protocol;
namespace SimCube.Roslynk.Server;

internal sealed class RequestDispatcher(RoslynkApplication application)
{
	public async Task<ResponseEnvelope> DispatchAsync(RequestEnvelope envelope, CancellationToken cancellationToken)
	{
		OperationResult result = envelope.Kind switch {
			RequestKind.GetCallers => await application.GetCallersAsync(Map(Wire.Deserialize<Transport.GetCallersRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ApplyCodeAction => await application.ApplyCodeActionAsync(Map(Wire.Deserialize<Transport.ApplyCodeActionRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ApplyCodeFix => await application.ApplyCodeFixAsync(Map(Wire.Deserialize<Transport.ApplyCodeFixRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetCodeActions => await application.GetCodeActionsAsync(Map(Wire.Deserialize<Transport.GetCodeActionsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindDeadConditionals => await application.FindDeadConditionalsAsync(Map(Wire.Deserialize<Transport.FindDeadConditionalsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindDeadCode => await application.FindDeadCodeAsync(Map(Wire.Deserialize<Transport.FindDeadCodeRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetDiagnostics => await application.GetDiagnosticsAsync(Map(Wire.Deserialize<Transport.GetDiagnosticsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.MultiQuery => await application.MultiQueryAsync(Map(Wire.Deserialize<Transport.MultiQueryRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ApplyPatch => await application.ApplyPatchAsync(Map(Wire.Deserialize<Transport.ApplyPatchRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ExtractMethod => await application.ExtractMethodAsync(Map(Wire.Deserialize<Transport.ExtractMethodRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindReads => await application.FindReadsAsync(Map(Wire.Deserialize<Transport.FindReadsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindReferences => await application.FindReferencesAsync(Map(Wire.Deserialize<Transport.FindReferencesRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindWrites => await application.FindWritesAsync(Map(Wire.Deserialize<Transport.FindWritesRequest>(envelope.Payload)), cancellationToken),
			RequestKind.RenameSymbol => await application.RenameSymbolAsync(Map(Wire.Deserialize<Transport.RenameSymbolRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ChangeSignature => await application.ChangeSignatureAsync(Map(Wire.Deserialize<Transport.ChangeSignatureRequest>(envelope.Payload)), cancellationToken),
			RequestKind.RenameParameter => await application.RenameParameterAsync(Map(Wire.Deserialize<Transport.RenameParameterRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetSolutionStatus => await application.GetSolutionStatusAsync(Map(Wire.Deserialize<Transport.GetSolutionStatusRequest>(envelope.Payload)), cancellationToken),
			RequestKind.OpenSolution => await application.OpenSolutionAsync(Map(Wire.Deserialize<Transport.OpenSolutionRequest>(envelope.Payload)), cancellationToken),
			RequestKind.ReloadSolution => await application.ReloadSolutionAsync(Map(Wire.Deserialize<Transport.ReloadSolutionRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindDefinition => await application.FindDefinitionAsync(Map(Wire.Deserialize<Transport.FindDefinitionRequest>(envelope.Payload)), cancellationToken),
			RequestKind.FindImplementations => await application.FindImplementationsAsync(Map(Wire.Deserialize<Transport.FindImplementationsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetExpressionInfo => await application.GetExpressionInfoAsync(Map(Wire.Deserialize<Transport.GetExpressionInfoRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetMembers => await application.GetMembersAsync(Map(Wire.Deserialize<Transport.GetMembersRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetSymbol => await application.GetSymbolAsync(Map(Wire.Deserialize<Transport.GetSymbolRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetSymbolBody => await application.GetSymbolBodyAsync(Map(Wire.Deserialize<Transport.GetSymbolBodyRequest>(envelope.Payload)), cancellationToken),
			RequestKind.GetTypeHierarchy => await application.GetTypeHierarchyAsync(Map(Wire.Deserialize<Transport.GetTypeHierarchyRequest>(envelope.Payload)), cancellationToken),
			RequestKind.SearchSymbols => await application.SearchSymbolsAsync(Map(Wire.Deserialize<Transport.SearchSymbolsRequest>(envelope.Payload)), cancellationToken),
			RequestKind.RemoveUnusedUsings => await application.RemoveUnusedUsingsAsync(Map(Wire.Deserialize<Transport.RemoveUnusedUsingsRequest>(envelope.Payload)), cancellationToken),
			_ => throw new ArgumentException("Unknown request kind.")
		};
		ProtocolError? error = result.Error is null ? null : new(result.Error.Code, result.Error.Message, result.Error.Candidates.ToArray(), result.Error.StaleFiles.ToArray());
		return new(envelope.RequestId, error is null ? ResponseStatus.Success : ResponseStatus.Error, Wire.Serialize(new TextResponse(result.Text)), error);
	}

	private static Domain.GetCallersRequest Map(Transport.GetCallersRequest request) => new(request.SolutionPath, request.MethodName);
	private static Domain.ApplyCodeActionRequest Map(Transport.ApplyCodeActionRequest request) => new(request.SolutionPath, request.ActionId, request.CheckOnly);
	private static Domain.ApplyCodeFixRequest Map(Transport.ApplyCodeFixRequest request) => new(request.SolutionPath, request.DocumentPath, request.DiagnosticId, request.Line, request.Column, request.CheckOnly);
	private static Domain.GetCodeActionsRequest Map(Transport.GetCodeActionsRequest request) => new(request.SolutionPath, request.DocumentPath, request.Line, request.Column, request.EndLine, request.EndColumn);
	private static Domain.FindDeadConditionalsRequest Map(Transport.FindDeadConditionalsRequest request) => new(request.SolutionPath);
	private static Domain.FindDeadCodeRequest Map(Transport.FindDeadCodeRequest request) => new(request.SolutionPath, request.Scope, request.IncludePublic, request.MaxResults);
	private static Domain.GetDiagnosticsRequest Map(Transport.GetDiagnosticsRequest request) => new(request.SolutionPath, request.IncludeErrors, request.IncludeWarnings, request.IncludeInfo, request.IncludeHidden, request.IncludeAnalyzers);
	private static Domain.MultiQueryRequest Map(Transport.MultiQueryRequest request) => new(request.SolutionPath, request.Operations.Select(MapQuery).ToArray(), request.ExpectSnapshot);
	private static Domain.ApplyPatchRequest Map(Transport.ApplyPatchRequest request) => new(request.SolutionPath, request.Patch, request.BaseVersions?.Select(version => new Domain.SourceVersion(version.Path, version.Version)).ToArray(), request.CheckOnly);
	private static Domain.ExtractMethodRequest Map(Transport.ExtractMethodRequest request) => new(request.SolutionPath, request.DocumentPath, request.StartLine, request.StartColumn, request.EndLine, request.EndColumn, request.MethodName, request.AsLocalFunction, request.CheckOnly);
	private static Domain.FindReadsRequest Map(Transport.FindReadsRequest request) => new(request.SolutionPath, request.SymbolName, request.MaxResults);
	private static Domain.FindReferencesRequest Map(Transport.FindReferencesRequest request) => new(request.SolutionPath, request.SymbolName, request.MaxResults);
	private static Domain.FindWritesRequest Map(Transport.FindWritesRequest request) => new(request.SolutionPath, request.SymbolName, request.MaxResults);
	private static Domain.RenameSymbolRequest Map(Transport.RenameSymbolRequest request) => new(request.SolutionPath, request.SymbolName, request.NewName, request.CheckOnly);
	private static Domain.ChangeSignatureRequest Map(Transport.ChangeSignatureRequest request) => new(request.SolutionPath, request.MethodId, request.ParameterType, request.ParameterName, request.DefaultValue, request.CallSiteArgument, request.CheckOnly);
	private static Domain.RenameParameterRequest Map(Transport.RenameParameterRequest request) => new(request.SolutionPath, request.MethodId, request.ParameterName, request.NewName, request.CheckOnly);
	private static Domain.GetSolutionStatusRequest Map(Transport.GetSolutionStatusRequest request) => new();
	private static Domain.OpenSolutionRequest Map(Transport.OpenSolutionRequest request) => new(request.SolutionPath);
	private static Domain.ReloadSolutionRequest Map(Transport.ReloadSolutionRequest request) => new(request.SolutionPath);
	private static Domain.FindDefinitionRequest Map(Transport.FindDefinitionRequest request) => new(request.SolutionPath, request.FilePath, request.Line, request.Column);
	private static Domain.FindImplementationsRequest Map(Transport.FindImplementationsRequest request) => new(request.SolutionPath, request.SymbolName);
	private static Domain.GetExpressionInfoRequest Map(Transport.GetExpressionInfoRequest request) => new(request.SolutionPath, request.FilePath, request.Line, request.Column);
	private static Domain.GetMembersRequest Map(Transport.GetMembersRequest request) => new(request.SolutionPath, request.TypeName, request.IncludeInherited, request.NameFilter, request.IncludeMethods, request.IncludeFields, request.IncludeProperties, request.IncludeEvents, request.IncludeNestedTypes);
	private static Domain.GetSymbolRequest Map(Transport.GetSymbolRequest request) => new(request.SolutionPath, request.SymbolName);
	private static Domain.GetSymbolBodyRequest Map(Transport.GetSymbolBodyRequest request) => new(request.SolutionPath, request.SymbolName, request.IncludeLeadingTrivia);
	private static Domain.GetTypeHierarchyRequest Map(Transport.GetTypeHierarchyRequest request) => new(request.SolutionPath, request.TypeName);
	private static Domain.SearchSymbolsRequest Map(Transport.SearchSymbolsRequest request) => new(request.SolutionPath, request.Query, request.MaxResults);
	private static Domain.RemoveUnusedUsingsRequest Map(Transport.RemoveUnusedUsingsRequest request) => new(request.SolutionPath, request.DocumentPath, request.CheckOnly);
	private static QueryOperation MapQuery(IQueryRequest request) => request switch {
		Transport.GetCallersRequest query => new GetCallersQuery(Map(query)),
		Transport.FindDeadConditionalsRequest query => new FindDeadConditionalsQuery(Map(query)),
		Transport.FindDeadCodeRequest query => new FindDeadCodeQuery(Map(query)),
		Transport.FindReadsRequest query => new FindReadsQuery(Map(query)),
		Transport.FindReferencesRequest query => new FindReferencesQuery(Map(query)),
		Transport.FindWritesRequest query => new FindWritesQuery(Map(query)),
		Transport.FindDefinitionRequest query => new FindDefinitionQuery(Map(query)),
		Transport.FindImplementationsRequest query => new FindImplementationsQuery(Map(query)),
		Transport.GetExpressionInfoRequest query => new GetExpressionInfoQuery(Map(query)),
		Transport.GetMembersRequest query => new GetMembersQuery(Map(query)),
		Transport.GetSymbolRequest query => new GetSymbolQuery(Map(query)),
		Transport.GetSymbolBodyRequest query => new GetSymbolBodyQuery(Map(query)),
		Transport.GetTypeHierarchyRequest query => new GetTypeHierarchyQuery(Map(query)),
		Transport.SearchSymbolsRequest query => new SearchSymbolsQuery(Map(query)),
		_ => throw new ArgumentException("Unsupported batch query.")
	};
}