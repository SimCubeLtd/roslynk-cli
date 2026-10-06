using MessagePack;

namespace SimCube.Roslynk.Protocol;

[Union(0, typeof(GetCallersRequest))]

[Union(1, typeof(FindDeadConditionalsRequest))]

[Union(2, typeof(FindDeadCodeRequest))]

[Union(3, typeof(FindReadsRequest))]

[Union(4, typeof(FindReferencesRequest))]

[Union(5, typeof(FindWritesRequest))]

[Union(6, typeof(FindDefinitionRequest))]

[Union(7, typeof(FindImplementationsRequest))]

[Union(8, typeof(GetExpressionInfoRequest))]

[Union(9, typeof(GetMembersRequest))]

[Union(10, typeof(GetSymbolRequest))]

[Union(11, typeof(GetSymbolBodyRequest))]

[Union(12, typeof(GetTypeHierarchyRequest))]

[Union(13, typeof(SearchSymbolsRequest))]

public interface IQueryRequest;

[MessagePackObject]
public sealed record GetCallersRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string MethodName) : IQueryRequest;

[MessagePackObject]
public sealed record ApplyCodeActionRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string ActionId, [property: Key(2)] bool CheckOnly = false);

[MessagePackObject]
public sealed record ApplyCodeFixRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string DocumentPath, [property: Key(2)] string DiagnosticId, [property: Key(3)] int Line, [property: Key(4)] int Column, [property: Key(5)] bool CheckOnly = false);

[MessagePackObject]
public sealed record GetCodeActionsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string DocumentPath, [property: Key(2)] int Line, [property: Key(3)] int Column, [property: Key(4)] int? EndLine = null, [property: Key(5)] int? EndColumn = null);

[MessagePackObject]
public sealed record FindDeadConditionalsRequest([property: Key(0)] string SolutionPath) : IQueryRequest;

[MessagePackObject]
public sealed record FindDeadCodeRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string? Scope = null, [property: Key(2)] bool IncludePublic = false, [property: Key(3)] int MaxResults = 50) : IQueryRequest;

[MessagePackObject]
public sealed record GetDiagnosticsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] bool IncludeErrors = false, [property: Key(2)] bool IncludeWarnings = false, [property: Key(3)] bool IncludeInfo = false, [property: Key(4)] bool IncludeHidden = false, [property: Key(5)] bool IncludeAnalyzers = true);

[MessagePackObject]
public sealed record MultiQueryRequest([property: Key(0)] string SolutionPath, [property: Key(1)] IQueryRequest[] Operations, [property: Key(2)] string? ExpectSnapshot = null);

[MessagePackObject]
public sealed record ApplyPatchRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string Patch, [property: Key(2)] SourceVersion[]? BaseVersions = null, [property: Key(3)] bool CheckOnly = false);

[MessagePackObject]
public sealed record ExtractMethodRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string DocumentPath, [property: Key(2)] int StartLine, [property: Key(3)] int StartColumn, [property: Key(4)] int EndLine, [property: Key(5)] int EndColumn, [property: Key(6)] string? MethodName = null, [property: Key(7)] bool AsLocalFunction = false, [property: Key(8)] bool CheckOnly = false);

[MessagePackObject]
public sealed record FindReadsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName, [property: Key(2)] int MaxResults = 100) : IQueryRequest;

[MessagePackObject]
public sealed record FindReferencesRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName, [property: Key(2)] int MaxResults = 100) : IQueryRequest;

[MessagePackObject]
public sealed record FindWritesRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName, [property: Key(2)] int MaxResults = 100) : IQueryRequest;

[MessagePackObject]
public sealed record RenameSymbolRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName, [property: Key(2)] string NewName, [property: Key(3)] bool CheckOnly = false);

[MessagePackObject]
public sealed record ChangeSignatureRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string MethodId, [property: Key(2)] string ParameterType, [property: Key(3)] string ParameterName, [property: Key(4)] string DefaultValue, [property: Key(5)] string? CallSiteArgument = null, [property: Key(6)] bool CheckOnly = false);

[MessagePackObject]
public sealed record RenameParameterRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string MethodId, [property: Key(2)] string ParameterName, [property: Key(3)] string NewName, [property: Key(4)] bool CheckOnly = false);

[MessagePackObject]
public sealed record GetSolutionStatusRequest();

[MessagePackObject]
public sealed record OpenSolutionRequest([property: Key(0)] string SolutionPath);

[MessagePackObject]
public sealed record ReloadSolutionRequest([property: Key(0)] string SolutionPath);

[MessagePackObject]
public sealed record FindDefinitionRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string FilePath, [property: Key(2)] int Line, [property: Key(3)] int Column) : IQueryRequest;

[MessagePackObject]
public sealed record FindImplementationsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName) : IQueryRequest;

[MessagePackObject]
public sealed record GetExpressionInfoRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string FilePath, [property: Key(2)] int Line, [property: Key(3)] int Column) : IQueryRequest;

[MessagePackObject]
public sealed record GetMembersRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string TypeName, [property: Key(2)] bool IncludeInherited = false, [property: Key(3)] string? NameFilter = null, [property: Key(4)] bool IncludeMethods = true, [property: Key(5)] bool IncludeFields = true, [property: Key(6)] bool IncludeProperties = true, [property: Key(7)] bool IncludeEvents = true, [property: Key(8)] bool IncludeNestedTypes = true) : IQueryRequest;

[MessagePackObject]
public sealed record GetSymbolRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName) : IQueryRequest;

[MessagePackObject]
public sealed record GetSymbolBodyRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string SymbolName, [property: Key(2)] bool IncludeLeadingTrivia = false, [property: Key(3)] bool Decompile = false) : IQueryRequest;

[MessagePackObject]
public sealed record GetTypeHierarchyRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string TypeName) : IQueryRequest;

[MessagePackObject]
public sealed record SearchSymbolsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string Query, [property: Key(2)] int MaxResults = 50) : IQueryRequest;

[MessagePackObject]
public sealed record RemoveUnusedUsingsRequest([property: Key(0)] string SolutionPath, [property: Key(1)] string? DocumentPath = null, [property: Key(2)] bool CheckOnly = false);
