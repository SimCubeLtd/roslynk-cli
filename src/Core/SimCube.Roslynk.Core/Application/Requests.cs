namespace SimCube.Roslynk.Core.Application;

public sealed record SourceVersion(string Path, string Version);

public abstract record QueryOperation;

public sealed record GetCallersRequest(string SolutionPath, string MethodName);

public sealed record GetCallersQuery(GetCallersRequest Request) : QueryOperation;

public sealed record ApplyCodeActionRequest(string SolutionPath, string ActionId, bool CheckOnly = false);

public sealed record ApplyCodeFixRequest(string SolutionPath, string DocumentPath, string DiagnosticId, int Line, int Column, bool CheckOnly = false);

public sealed record GetCodeActionsRequest(string SolutionPath, string DocumentPath, int Line, int Column, int? EndLine = null, int? EndColumn = null);

public sealed record FindDeadConditionalsRequest(string SolutionPath);

public sealed record FindDeadConditionalsQuery(FindDeadConditionalsRequest Request) : QueryOperation;

public sealed record FindDeadCodeRequest(string SolutionPath, string? Scope = null, bool IncludePublic = false, int MaxResults = 50);

public sealed record FindDeadCodeQuery(FindDeadCodeRequest Request) : QueryOperation;

public sealed record GetDiagnosticsRequest(string SolutionPath, bool IncludeErrors = false, bool IncludeWarnings = false, bool IncludeInfo = false, bool IncludeHidden = false, bool IncludeAnalyzers = true);

public sealed record MultiQueryRequest(string SolutionPath, IReadOnlyList<QueryOperation> Operations, string? ExpectSnapshot = null);

public sealed record ApplyPatchRequest(string SolutionPath, string Patch, IReadOnlyList<SourceVersion>? BaseVersions = null, bool CheckOnly = false);

public sealed record ExtractMethodRequest(string SolutionPath, string DocumentPath, int StartLine, int StartColumn, int EndLine, int EndColumn, string? MethodName = null, bool AsLocalFunction = false, bool CheckOnly = false);

public sealed record FindReadsRequest(string SolutionPath, string SymbolName, int MaxResults = 100);

public sealed record FindReadsQuery(FindReadsRequest Request) : QueryOperation;

public sealed record FindReferencesRequest(string SolutionPath, string SymbolName, int MaxResults = 100);

public sealed record FindReferencesQuery(FindReferencesRequest Request) : QueryOperation;

public sealed record FindWritesRequest(string SolutionPath, string SymbolName, int MaxResults = 100);

public sealed record FindWritesQuery(FindWritesRequest Request) : QueryOperation;

public sealed record RenameSymbolRequest(string SolutionPath, string SymbolName, string NewName, bool CheckOnly = false);

public sealed record ChangeSignatureRequest(string SolutionPath, string MethodId, string ParameterType, string ParameterName, string DefaultValue, string? CallSiteArgument = null, bool CheckOnly = false);

public sealed record RenameParameterRequest(string SolutionPath, string MethodId, string ParameterName, string NewName, bool CheckOnly = false);

public sealed record GetSolutionStatusRequest();

public sealed record OpenSolutionRequest(string SolutionPath);

public sealed record ReloadSolutionRequest(string SolutionPath);

public sealed record FindDefinitionRequest(string SolutionPath, string FilePath, int Line, int Column);

public sealed record FindDefinitionQuery(FindDefinitionRequest Request) : QueryOperation;

public sealed record FindImplementationsRequest(string SolutionPath, string SymbolName);

public sealed record FindImplementationsQuery(FindImplementationsRequest Request) : QueryOperation;

public sealed record GetExpressionInfoRequest(string SolutionPath, string FilePath, int Line, int Column);

public sealed record GetExpressionInfoQuery(GetExpressionInfoRequest Request) : QueryOperation;

public sealed record GetMembersRequest(string SolutionPath, string TypeName, bool IncludeInherited = false, string? NameFilter = null, bool IncludeMethods = true, bool IncludeFields = true, bool IncludeProperties = true, bool IncludeEvents = true, bool IncludeNestedTypes = true);

public sealed record GetMembersQuery(GetMembersRequest Request) : QueryOperation;

public sealed record GetSymbolRequest(string SolutionPath, string SymbolName);

public sealed record GetSymbolQuery(GetSymbolRequest Request) : QueryOperation;

public sealed record GetSymbolBodyRequest(string SolutionPath, string SymbolName, bool IncludeLeadingTrivia = false);

public sealed record GetSymbolBodyQuery(GetSymbolBodyRequest Request) : QueryOperation;

public sealed record GetTypeHierarchyRequest(string SolutionPath, string TypeName);

public sealed record GetTypeHierarchyQuery(GetTypeHierarchyRequest Request) : QueryOperation;

public sealed record SearchSymbolsRequest(string SolutionPath, string Query, int MaxResults = 50);

public sealed record SearchSymbolsQuery(SearchSymbolsRequest Request) : QueryOperation;

public sealed record RemoveUnusedUsingsRequest(string SolutionPath, string? DocumentPath = null, bool CheckOnly = false);
