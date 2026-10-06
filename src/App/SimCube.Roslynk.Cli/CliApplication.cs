using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Text.Json;
using SimCube.Roslynk.Protocol;
namespace SimCube.Roslynk.Cli;

internal sealed class CliApplication
{
	private readonly LocalEndpoint Endpoint;
	private readonly TextWriter Output;
	private readonly TextWriter Error;
	private readonly string WorkingDirectory;
	private readonly Option<string?> Solution = new("--solution") { Description = "Solution path; discovered from the current directory when omitted.", Recursive = true };
	private readonly Option<bool> Json = new("--json") { Description = "Print a single structured response.", Recursive = true };
	private readonly Option<bool> Timing = new("--timing") { Description = "Write connection and command timing to stderr.", Recursive = true };
	private readonly Dictionary<string, Func<ParseResult, IQueryRequest>> Queries = new(StringComparer.Ordinal);
	private readonly Command Root;
	public CliApplication(LocalEndpoint? endpoint = null, TextWriter? output = null, TextWriter? error = null, string? workingDirectory = null)
	{
		Endpoint = endpoint ?? new();
		Output = output ?? Console.Out;
		Error = error ?? Console.Error;
		WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
		Root = new("roslynk-cli", "Semantic C# queries and refactors using a warm local daemon.");
		Root.Options.Add(new HelpOption());
		Root.Options.Add(new VersionOption());
		Root.Options.Add(Solution); Root.Options.Add(Json); Root.Options.Add(Timing);
		var solution = new Command("solution", "Manage loaded solutions.");
		Root.Subcommands.Add(solution);
		AddCommands(solution);
		AddServerCommands();
	}
	public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
	{
		try
		{
			ParseResult parse = Root.Parse(args);
			if (parse.Errors.Count > 0)
			{
				foreach (ParseError issue in parse.Errors) await Error.WriteLineAsync(issue.Message);
				return 2;
			}
			return await parse.InvokeAsync(new InvocationConfiguration { Output = Output, Error = Error, EnableDefaultExceptionHandler = false }, cancellationToken);
		}
		catch (OperationCanceledException) { await Error.WriteLineAsync("Cancelled."); return 130; }
		catch (ArgumentException exception) { await Error.WriteLineAsync(exception.Message); return 2; }
		catch (Exception exception) { await Error.WriteLineAsync(exception.Message); return 1; }
	}
	private string? SourcePath(string? path)
	{
		if (path is null) return null;
		string full = Path.GetFullPath(path, WorkingDirectory);
		return File.Exists(full) ? full : path;
	}
	private string PathFor(ParseResult parse) => SolutionDiscovery.Find(parse.GetValue(Solution), WorkingDirectory);
	private async Task<int> ExecuteAsync<T>(ParseResult parse, RequestKind kind, T request, CancellationToken cancellationToken)
	{
		var watch = System.Diagnostics.Stopwatch.StartNew();
		await using IpcClient client = await IpcClient.ConnectAsync(Endpoint, autoStart: true, cancellationToken);
		double connect = watch.Elapsed.TotalMilliseconds;
		ResponseEnvelope response = await client.SendAsync(kind, request, cancellationToken);
		if (parse.GetValue(Timing)) await Error.WriteLineAsync($"connect_ms={connect:F2} request_ms={watch.Elapsed.TotalMilliseconds - connect:F2} total_ms={watch.Elapsed.TotalMilliseconds:F2}");
		if (response.Status != ResponseStatus.Success)
		{
			await Error.WriteLineAsync($"{response.Error?.Code}: {response.Error?.Message}");
			foreach (string candidate in response.Error?.Candidates ?? []) await Error.WriteLineAsync("candidate=" + candidate);
			foreach (string stale in response.Error?.StaleFiles ?? []) await Error.WriteLineAsync("stale=" + stale);
			return response.Error?.Code == "Invalid" ? 2 : 1;
		}
		TextResponse text = Wire.Deserialize<TextResponse>(response.Payload);
		if (parse.GetValue(Json)) await Output.WriteLineAsync(JsonSerializer.Serialize(text));
		else await Output.WriteAsync(text.Text);
		// Diagnostics remain successful output, but compiler errors are a failed check for shell scripts.
		return kind == RequestKind.GetDiagnostics && text.Text.Split('\n').Any(line => line.StartsWith("errors=") && int.TryParse(line[7..], out int count) && count > 0) ? 1 : 0;
	}
	private void AddCommands(Command solution)
	{
		var getCallers = new Command("callers", "Find members that call a method.");
		var getCallersMethodName = new Argument<string>("method-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		getCallers.Arguments.Add(getCallersMethodName);
		getCallers.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetCallers, new GetCallersRequest(PathFor(parse), parse.GetValue(getCallersMethodName)!), token));
		Queries["callers"] = parse => new GetCallersRequest(PathFor(parse), parse.GetValue(getCallersMethodName)!);
		Root.Subcommands.Add(getCallers);

		var applyCodeAction = new Command("apply-action", "Apply a previously discovered code action.");
		var applyCodeActionActionId = new Argument<string>("action-id") { Description = "Opaque action ID from actions or a fix conflict." };
		applyCodeAction.Arguments.Add(applyCodeActionActionId);
		var applyCodeActionCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		applyCodeAction.Options.Add(applyCodeActionCheckOnly);
		applyCodeAction.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.ApplyCodeAction, new ApplyCodeActionRequest(PathFor(parse), parse.GetValue(applyCodeActionActionId)!, parse.GetValue(applyCodeActionCheckOnly)), token));
		Root.Subcommands.Add(applyCodeAction);

		var applyCodeFix = new Command("fix", "Apply a diagnostic fix at a source position.");
		var applyCodeFixDocumentPath = new Argument<string>("document-path") { Description = "Source file; current-directory paths or solution-relative paths." };
		applyCodeFix.Arguments.Add(applyCodeFixDocumentPath);
		var applyCodeFixDiagnosticId = new Argument<string>("diagnostic-id") { Description = "Compiler/analyzer diagnostic ID, e.g. CS0219." };
		applyCodeFix.Arguments.Add(applyCodeFixDiagnosticId);
		var applyCodeFixLine = new Argument<int>("line") { Description = "line; source positions are 1-based." };
		applyCodeFix.Arguments.Add(applyCodeFixLine);
		var applyCodeFixColumn = new Argument<int>("column") { Description = "column; source positions are 1-based." };
		applyCodeFix.Arguments.Add(applyCodeFixColumn);
		var applyCodeFixCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		applyCodeFix.Options.Add(applyCodeFixCheckOnly);
		applyCodeFix.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.ApplyCodeFix, new ApplyCodeFixRequest(PathFor(parse), SourcePath(parse.GetValue(applyCodeFixDocumentPath))!, parse.GetValue(applyCodeFixDiagnosticId)!, parse.GetValue(applyCodeFixLine), parse.GetValue(applyCodeFixColumn), parse.GetValue(applyCodeFixCheckOnly)), token));
		Root.Subcommands.Add(applyCodeFix);

		var getCodeActions = new Command("actions", "List fixes and refactors at a source position.");
		var getCodeActionsDocumentPath = new Argument<string>("document-path") { Description = "Source file; current-directory paths or solution-relative paths." };
		getCodeActions.Arguments.Add(getCodeActionsDocumentPath);
		var getCodeActionsLine = new Argument<int>("line") { Description = "line; source positions are 1-based." };
		getCodeActions.Arguments.Add(getCodeActionsLine);
		var getCodeActionsColumn = new Argument<int>("column") { Description = "column; source positions are 1-based." };
		getCodeActions.Arguments.Add(getCodeActionsColumn);
		var getCodeActionsEndLine = new Option<int?>("--end-line") { Description = "end-line.", DefaultValueFactory = _ => null };
		getCodeActions.Options.Add(getCodeActionsEndLine);
		var getCodeActionsEndColumn = new Option<int?>("--end-column") { Description = "end-column.", DefaultValueFactory = _ => null };
		getCodeActions.Options.Add(getCodeActionsEndColumn);
		getCodeActions.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetCodeActions, new GetCodeActionsRequest(PathFor(parse), SourcePath(parse.GetValue(getCodeActionsDocumentPath))!, parse.GetValue(getCodeActionsLine), parse.GetValue(getCodeActionsColumn), parse.GetValue(getCodeActionsEndLine), parse.GetValue(getCodeActionsEndColumn)), token));
		Root.Subcommands.Add(getCodeActions);

		var findDeadConditionals = new Command("dead-conditionals", "Find conditional branches never compiled by loaded configurations.");
		findDeadConditionals.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindDeadConditionals, new FindDeadConditionalsRequest(PathFor(parse)), token));
		Queries["dead-conditionals"] = parse => new FindDeadConditionalsRequest(PathFor(parse));
		Root.Subcommands.Add(findDeadConditionals);

		var findDeadCode = new Command("dead-code", "Find unreferenced members with confidence and reasons.");
		var findDeadCodeScope = new Option<string?>("--scope") { Description = "scope.", DefaultValueFactory = _ => null };
		findDeadCode.Options.Add(findDeadCodeScope);
		var findDeadCodeIncludePublic = new Option<bool>("--public") { Description = "include-public.", DefaultValueFactory = _ => false };
		findDeadCode.Options.Add(findDeadCodeIncludePublic);
		var findDeadCodeMaxResults = new Option<int>("--max-results") { Description = "max-results.", DefaultValueFactory = _ => 50 };
		findDeadCode.Options.Add(findDeadCodeMaxResults);
		findDeadCode.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindDeadCode, new FindDeadCodeRequest(PathFor(parse), parse.GetValue(findDeadCodeScope), parse.GetValue(findDeadCodeIncludePublic), parse.GetValue(findDeadCodeMaxResults)), token));
		Queries["dead-code"] = parse => new FindDeadCodeRequest(PathFor(parse), parse.GetValue(findDeadCodeScope), parse.GetValue(findDeadCodeIncludePublic), parse.GetValue(findDeadCodeMaxResults));
		Root.Subcommands.Add(findDeadCode);

		var getDiagnostics = new Command("diagnostics", "Report compiler and analyzer diagnostics.");
		getDiagnostics.Aliases.Add("diag");
		var getDiagnosticsIncludeErrors = new Option<bool>("--errors") { Description = "include-errors.", DefaultValueFactory = _ => true };
		getDiagnostics.Options.Add(getDiagnosticsIncludeErrors);
		var getDiagnosticsIncludeWarnings = new Option<bool>("--warnings") { Description = "include-warnings.", DefaultValueFactory = _ => true };
		getDiagnostics.Options.Add(getDiagnosticsIncludeWarnings);
		var getDiagnosticsIncludeInfo = new Option<bool>("--info") { Description = "include-info.", DefaultValueFactory = _ => false };
		getDiagnostics.Options.Add(getDiagnosticsIncludeInfo);
		var getDiagnosticsIncludeHidden = new Option<bool>("--hidden") { Description = "include-hidden.", DefaultValueFactory = _ => false };
		getDiagnostics.Options.Add(getDiagnosticsIncludeHidden);
		var getDiagnosticsIncludeAnalyzers = new Option<bool>("--analyzers") { Description = "include-analyzers.", DefaultValueFactory = _ => true };
		getDiagnostics.Options.Add(getDiagnosticsIncludeAnalyzers);
		getDiagnostics.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetDiagnostics, new GetDiagnosticsRequest(PathFor(parse), parse.GetValue(getDiagnosticsIncludeErrors), parse.GetValue(getDiagnosticsIncludeWarnings), parse.GetValue(getDiagnosticsIncludeInfo), parse.GetValue(getDiagnosticsIncludeHidden), parse.GetValue(getDiagnosticsIncludeAnalyzers)), token));
		Root.Subcommands.Add(getDiagnostics);

		var extractMethod = new Command("extract", "Extract a selected expression or statements into a method.");
		var extractMethodDocumentPath = new Argument<string>("document-path") { Description = "Source file; current-directory paths or solution-relative paths." };
		extractMethod.Arguments.Add(extractMethodDocumentPath);
		var extractMethodStartLine = new Argument<int>("start-line") { Description = "start-line; source positions are 1-based." };
		extractMethod.Arguments.Add(extractMethodStartLine);
		var extractMethodStartColumn = new Argument<int>("start-column") { Description = "start-column; source positions are 1-based." };
		extractMethod.Arguments.Add(extractMethodStartColumn);
		var extractMethodEndLine = new Argument<int>("end-line") { Description = "end-line; source positions are 1-based." };
		extractMethod.Arguments.Add(extractMethodEndLine);
		var extractMethodEndColumn = new Argument<int>("end-column") { Description = "end-column; source positions are 1-based." };
		extractMethod.Arguments.Add(extractMethodEndColumn);
		var extractMethodMethodName = new Option<string?>("--method-name") { Description = "method-name.", DefaultValueFactory = _ => null };
		extractMethod.Options.Add(extractMethodMethodName);
		var extractMethodAsLocalFunction = new Option<bool>("--local-function") { Description = "as-local-function.", DefaultValueFactory = _ => false };
		extractMethod.Options.Add(extractMethodAsLocalFunction);
		var extractMethodCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		extractMethod.Options.Add(extractMethodCheckOnly);
		extractMethod.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.ExtractMethod, new ExtractMethodRequest(PathFor(parse), SourcePath(parse.GetValue(extractMethodDocumentPath))!, parse.GetValue(extractMethodStartLine), parse.GetValue(extractMethodStartColumn), parse.GetValue(extractMethodEndLine), parse.GetValue(extractMethodEndColumn), parse.GetValue(extractMethodMethodName), parse.GetValue(extractMethodAsLocalFunction), parse.GetValue(extractMethodCheckOnly)), token));
		Root.Subcommands.Add(extractMethod);

		var findReads = new Command("reads", "Find reads of a field, property or parameter.");
		var findReadsSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		findReads.Arguments.Add(findReadsSymbolName);
		var findReadsMaxResults = new Option<int>("--max-results") { Description = "max-results.", DefaultValueFactory = _ => 100 };
		findReads.Options.Add(findReadsMaxResults);
		findReads.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindReads, new FindReadsRequest(PathFor(parse), parse.GetValue(findReadsSymbolName)!, parse.GetValue(findReadsMaxResults)), token));
		Queries["reads"] = parse => new FindReadsRequest(PathFor(parse), parse.GetValue(findReadsSymbolName)!, parse.GetValue(findReadsMaxResults));
		Root.Subcommands.Add(findReads);

		var findReferences = new Command("refs", "Find symbol usages across source and conditional branches.");
		var findReferencesSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		findReferences.Arguments.Add(findReferencesSymbolName);
		var findReferencesMaxResults = new Option<int>("--max-results") { Description = "max-results.", DefaultValueFactory = _ => 100 };
		findReferences.Options.Add(findReferencesMaxResults);
		findReferences.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindReferences, new FindReferencesRequest(PathFor(parse), parse.GetValue(findReferencesSymbolName)!, parse.GetValue(findReferencesMaxResults)), token));
		Queries["refs"] = parse => new FindReferencesRequest(PathFor(parse), parse.GetValue(findReferencesSymbolName)!, parse.GetValue(findReferencesMaxResults));
		Root.Subcommands.Add(findReferences);

		var findWrites = new Command("writes", "Find writes to a field, property or parameter.");
		var findWritesSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		findWrites.Arguments.Add(findWritesSymbolName);
		var findWritesMaxResults = new Option<int>("--max-results") { Description = "max-results.", DefaultValueFactory = _ => 100 };
		findWrites.Options.Add(findWritesMaxResults);
		findWrites.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindWrites, new FindWritesRequest(PathFor(parse), parse.GetValue(findWritesSymbolName)!, parse.GetValue(findWritesMaxResults)), token));
		Queries["writes"] = parse => new FindWritesRequest(PathFor(parse), parse.GetValue(findWritesSymbolName)!, parse.GetValue(findWritesMaxResults));
		Root.Subcommands.Add(findWrites);

		var renameSymbol = new Command("rename", "Rename a symbol and all semantic references.");
		var renameSymbolSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		renameSymbol.Arguments.Add(renameSymbolSymbolName);
		var renameSymbolNewName = new Argument<string>("new-name") { Description = "New C# identifier." };
		renameSymbol.Arguments.Add(renameSymbolNewName);
		var renameSymbolCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		renameSymbol.Options.Add(renameSymbolCheckOnly);
		renameSymbol.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.RenameSymbol, new RenameSymbolRequest(PathFor(parse), parse.GetValue(renameSymbolSymbolName)!, parse.GetValue(renameSymbolNewName)!, parse.GetValue(renameSymbolCheckOnly)), token));
		Root.Subcommands.Add(renameSymbol);

		var changeSignature = new Command("change-signature", "Append an optional parameter and update callers.");
		var changeSignatureMethodId = new Argument<string>("method-id") { Description = "Fully-qualified name; add parameter types to select an overload." };
		changeSignature.Arguments.Add(changeSignatureMethodId);
		var changeSignatureParameterType = new Argument<string>("parameter-type") { Description = "parameter-type; source positions are 1-based." };
		changeSignature.Arguments.Add(changeSignatureParameterType);
		var changeSignatureParameterName = new Argument<string>("parameter-name") { Description = "parameter-name; source positions are 1-based." };
		changeSignature.Arguments.Add(changeSignatureParameterName);
		var changeSignatureDefaultValue = new Argument<string>("default-value") { Description = "default-value; source positions are 1-based." };
		changeSignature.Arguments.Add(changeSignatureDefaultValue);
		var changeSignatureCallSiteArgument = new Option<string?>("--call-site-argument") { Description = "call-site-argument.", DefaultValueFactory = _ => null };
		changeSignature.Options.Add(changeSignatureCallSiteArgument);
		var changeSignatureCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		changeSignature.Options.Add(changeSignatureCheckOnly);
		changeSignature.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.ChangeSignature, new ChangeSignatureRequest(PathFor(parse), parse.GetValue(changeSignatureMethodId)!, parse.GetValue(changeSignatureParameterType)!, parse.GetValue(changeSignatureParameterName)!, parse.GetValue(changeSignatureDefaultValue)!, parse.GetValue(changeSignatureCallSiteArgument), parse.GetValue(changeSignatureCheckOnly)), token));
		Root.Subcommands.Add(changeSignature);

		var renameParameter = new Command("rename-parameter", "Rename a parameter and named arguments across its declaration family.");
		var renameParameterMethodId = new Argument<string>("method-id") { Description = "Fully-qualified name; add parameter types to select an overload." };
		renameParameter.Arguments.Add(renameParameterMethodId);
		var renameParameterParameterName = new Argument<string>("parameter-name") { Description = "parameter-name; source positions are 1-based." };
		renameParameter.Arguments.Add(renameParameterParameterName);
		var renameParameterNewName = new Argument<string>("new-name") { Description = "New C# identifier." };
		renameParameter.Arguments.Add(renameParameterNewName);
		var renameParameterCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		renameParameter.Options.Add(renameParameterCheckOnly);
		renameParameter.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.RenameParameter, new RenameParameterRequest(PathFor(parse), parse.GetValue(renameParameterMethodId)!, parse.GetValue(renameParameterParameterName)!, parse.GetValue(renameParameterNewName)!, parse.GetValue(renameParameterCheckOnly)), token));
		Root.Subcommands.Add(renameParameter);

		var getSolutionStatus = new Command("status", "List loaded solutions and loading progress.");
		getSolutionStatus.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetSolutionStatus, new GetSolutionStatusRequest(), token));
		solution.Subcommands.Add(getSolutionStatus);

		var openSolution = new Command("open", "Start loading a solution in the background.");
		openSolution.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.OpenSolution, new OpenSolutionRequest(PathFor(parse)), token));
		solution.Subcommands.Add(openSolution);

		var reloadSolution = new Command("reload", "Explicitly reload a solution from disk.");
		reloadSolution.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.ReloadSolution, new ReloadSolutionRequest(PathFor(parse)), token));
		solution.Subcommands.Add(reloadSolution);

		var findDefinition = new Command("definition", "Resolve the definition at a source position.");
		var findDefinitionFilePath = new Argument<string>("file-path") { Description = "Source file; current-directory paths or solution-relative paths." };
		findDefinition.Arguments.Add(findDefinitionFilePath);
		var findDefinitionLine = new Argument<int>("line") { Description = "line; source positions are 1-based." };
		findDefinition.Arguments.Add(findDefinitionLine);
		var findDefinitionColumn = new Argument<int>("column") { Description = "column; source positions are 1-based." };
		findDefinition.Arguments.Add(findDefinitionColumn);
		findDefinition.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindDefinition, new FindDefinitionRequest(PathFor(parse), SourcePath(parse.GetValue(findDefinitionFilePath))!, parse.GetValue(findDefinitionLine), parse.GetValue(findDefinitionColumn)), token));
		Queries["definition"] = parse => new FindDefinitionRequest(PathFor(parse), SourcePath(parse.GetValue(findDefinitionFilePath))!, parse.GetValue(findDefinitionLine), parse.GetValue(findDefinitionColumn));
		Root.Subcommands.Add(findDefinition);

		var findImplementations = new Command("implementations", "Find interface implementations and member overrides.");
		var findImplementationsSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		findImplementations.Arguments.Add(findImplementationsSymbolName);
		findImplementations.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.FindImplementations, new FindImplementationsRequest(PathFor(parse), parse.GetValue(findImplementationsSymbolName)!), token));
		Queries["implementations"] = parse => new FindImplementationsRequest(PathFor(parse), parse.GetValue(findImplementationsSymbolName)!);
		Root.Subcommands.Add(findImplementations);

		var getExpressionInfo = new Command("expression", "Report type, binding, conversion and nullability at a position.");
		var getExpressionInfoFilePath = new Argument<string>("file-path") { Description = "Source file; current-directory paths or solution-relative paths." };
		getExpressionInfo.Arguments.Add(getExpressionInfoFilePath);
		var getExpressionInfoLine = new Argument<int>("line") { Description = "line; source positions are 1-based." };
		getExpressionInfo.Arguments.Add(getExpressionInfoLine);
		var getExpressionInfoColumn = new Argument<int>("column") { Description = "column; source positions are 1-based." };
		getExpressionInfo.Arguments.Add(getExpressionInfoColumn);
		getExpressionInfo.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetExpressionInfo, new GetExpressionInfoRequest(PathFor(parse), SourcePath(parse.GetValue(getExpressionInfoFilePath))!, parse.GetValue(getExpressionInfoLine), parse.GetValue(getExpressionInfoColumn)), token));
		Queries["expression"] = parse => new GetExpressionInfoRequest(PathFor(parse), SourcePath(parse.GetValue(getExpressionInfoFilePath))!, parse.GetValue(getExpressionInfoLine), parse.GetValue(getExpressionInfoColumn));
		Root.Subcommands.Add(getExpressionInfo);

		var getMembers = new Command("members", "List members, including local functions.");
		var getMembersTypeName = new Argument<string>("type-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		getMembers.Arguments.Add(getMembersTypeName);
		var getMembersIncludeInherited = new Option<bool>("--inherited") { Description = "include-inherited.", DefaultValueFactory = _ => false };
		getMembers.Options.Add(getMembersIncludeInherited);
		var getMembersNameFilter = new Option<string?>("--filter") { Description = "name-filter.", DefaultValueFactory = _ => null };
		getMembers.Options.Add(getMembersNameFilter);
		var getMembersIncludeMethods = new Option<bool>("--include-methods") { Description = "include-methods.", DefaultValueFactory = _ => true };
		getMembers.Options.Add(getMembersIncludeMethods);
		var getMembersIncludeFields = new Option<bool>("--include-fields") { Description = "include-fields.", DefaultValueFactory = _ => true };
		getMembers.Options.Add(getMembersIncludeFields);
		var getMembersIncludeProperties = new Option<bool>("--include-properties") { Description = "include-properties.", DefaultValueFactory = _ => true };
		getMembers.Options.Add(getMembersIncludeProperties);
		var getMembersIncludeEvents = new Option<bool>("--include-events") { Description = "include-events.", DefaultValueFactory = _ => true };
		getMembers.Options.Add(getMembersIncludeEvents);
		var getMembersIncludeNestedTypes = new Option<bool>("--include-nested-types") { Description = "include-nested-types.", DefaultValueFactory = _ => true };
		getMembers.Options.Add(getMembersIncludeNestedTypes);
		getMembers.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetMembers, new GetMembersRequest(PathFor(parse), parse.GetValue(getMembersTypeName)!, parse.GetValue(getMembersIncludeInherited), parse.GetValue(getMembersNameFilter), parse.GetValue(getMembersIncludeMethods), parse.GetValue(getMembersIncludeFields), parse.GetValue(getMembersIncludeProperties), parse.GetValue(getMembersIncludeEvents), parse.GetValue(getMembersIncludeNestedTypes)), token));
		Queries["members"] = parse => new GetMembersRequest(PathFor(parse), parse.GetValue(getMembersTypeName)!, parse.GetValue(getMembersIncludeInherited), parse.GetValue(getMembersNameFilter), parse.GetValue(getMembersIncludeMethods), parse.GetValue(getMembersIncludeFields), parse.GetValue(getMembersIncludeProperties), parse.GetValue(getMembersIncludeEvents), parse.GetValue(getMembersIncludeNestedTypes));
		Root.Subcommands.Add(getMembers);

		var getSymbol = new Command("symbol", "Resolve a symbol and show its declaration.");
		var getSymbolSymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		getSymbol.Arguments.Add(getSymbolSymbolName);
		getSymbol.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetSymbol, new GetSymbolRequest(PathFor(parse), parse.GetValue(getSymbolSymbolName)!), token));
		Queries["symbol"] = parse => new GetSymbolRequest(PathFor(parse), parse.GetValue(getSymbolSymbolName)!);
		Root.Subcommands.Add(getSymbol);

		var getSymbolBody = new Command("body", "Read complete declarations and bodies verbatim.");
		var getSymbolBodySymbolName = new Argument<string>("symbol-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		getSymbolBody.Arguments.Add(getSymbolBodySymbolName);
		var getSymbolBodyIncludeLeadingTrivia = new Option<bool>("--leading-trivia") { Description = "include-leading-trivia.", DefaultValueFactory = _ => false };
		getSymbolBody.Options.Add(getSymbolBodyIncludeLeadingTrivia);
		getSymbolBody.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetSymbolBody, new GetSymbolBodyRequest(PathFor(parse), parse.GetValue(getSymbolBodySymbolName)!, parse.GetValue(getSymbolBodyIncludeLeadingTrivia)), token));
		Queries["body"] = parse => new GetSymbolBodyRequest(PathFor(parse), parse.GetValue(getSymbolBodySymbolName)!, parse.GetValue(getSymbolBodyIncludeLeadingTrivia));
		Root.Subcommands.Add(getSymbolBody);

		var getTypeHierarchy = new Command("hierarchy", "Show base types, interfaces and derived types.");
		var getTypeHierarchyTypeName = new Argument<string>("type-name") { Description = "Fully-qualified name; add parameter types to select an overload." };
		getTypeHierarchy.Arguments.Add(getTypeHierarchyTypeName);
		getTypeHierarchy.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.GetTypeHierarchy, new GetTypeHierarchyRequest(PathFor(parse), parse.GetValue(getTypeHierarchyTypeName)!), token));
		Queries["hierarchy"] = parse => new GetTypeHierarchyRequest(PathFor(parse), parse.GetValue(getTypeHierarchyTypeName)!);
		Root.Subcommands.Add(getTypeHierarchy);

		var searchSymbols = new Command("search", "Search declared symbols by partial name.");
		var searchSymbolsQuery = new Argument<string>("query") { Description = "Partial declared symbol name." };
		searchSymbols.Arguments.Add(searchSymbolsQuery);
		var searchSymbolsMaxResults = new Option<int>("--max-results") { Description = "max-results.", DefaultValueFactory = _ => 50 };
		searchSymbols.Options.Add(searchSymbolsMaxResults);
		searchSymbols.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.SearchSymbols, new SearchSymbolsRequest(PathFor(parse), parse.GetValue(searchSymbolsQuery)!, parse.GetValue(searchSymbolsMaxResults)), token));
		Queries["search"] = parse => new SearchSymbolsRequest(PathFor(parse), parse.GetValue(searchSymbolsQuery)!, parse.GetValue(searchSymbolsMaxResults));
		Root.Subcommands.Add(searchSymbols);

		var removeUnusedUsings = new Command("usings", "Remove unused usings from a document or solution.");
		var removeUnusedUsingsDocumentPath = new Option<string?>("--document-path") { Description = "document-path.", DefaultValueFactory = _ => null };
		removeUnusedUsings.Options.Add(removeUnusedUsingsDocumentPath);
		var removeUnusedUsingsCheckOnly = new Option<bool>("--check-only") { Description = "check-only.", DefaultValueFactory = _ => false };
		removeUnusedUsings.Options.Add(removeUnusedUsingsCheckOnly);
		removeUnusedUsings.SetAction((parse, token) => ExecuteAsync(parse, RequestKind.RemoveUnusedUsings, new RemoveUnusedUsingsRequest(PathFor(parse), SourcePath(parse.GetValue(removeUnusedUsingsDocumentPath)), parse.GetValue(removeUnusedUsingsCheckOnly)), token));
		Root.Subcommands.Add(removeUnusedUsings);

		var patch = new Command("patch", "Apply a unified diff from a file, or - for stdin.");
		var patchFile = new Argument<string>("file");
		var check = new Option<bool>("--check-only");
		var versions = new Option<string[]>("--base-version") { Description = "Repeat PATH=SHA256 for disk stale guards." };
		patch.Arguments.Add(patchFile); patch.Options.Add(check); patch.Options.Add(versions);
		patch.SetAction(async (parse, token) =>
		{
			string file = parse.GetValue(patchFile)!;
			string diff = file == "-" ? await Console.In.ReadToEndAsync(token) : await File.ReadAllTextAsync(System.IO.Path.GetFullPath(file, WorkingDirectory), token);
			SourceVersion[]? guards = parse.GetValue(versions)?.Select(value =>
	  {
		  int equals = value.LastIndexOf('=');
		  if (equals <= 0) throw new ArgumentException("Base version must be PATH=SHA256.");
		  return new SourceVersion(value[..equals], value[(equals + 1)..]);
	  }).ToArray();
			return await ExecuteAsync(parse, RequestKind.ApplyPatch, new ApplyPatchRequest(PathFor(parse), diff, guards, parse.GetValue(check)), token);
		});
		Root.Subcommands.Add(patch);

		var batch = new Command("batch", "Run quoted read-only commands on one pinned snapshot, e.g. batch 'refs N.T.M' 'callers N.T.M'.");
		var commands = new Argument<string[]>("commands") { Arity = ArgumentArity.OneOrMore };
		var snapshot = new Option<string?>("--expect-snapshot");
		batch.Arguments.Add(commands); batch.Options.Add(snapshot);
		batch.SetAction((parse, token) =>
		{
			string path = PathFor(parse);
			IQueryRequest[] queries = parse.GetValue(commands)!.Select(command =>
	  {
		  ParseResult nested = Root.Parse(command);
		  if (nested.Errors.Count > 0) throw new ArgumentException(string.Join("; ", nested.Errors.Select(error => error.Message)));
		  if (!Queries.TryGetValue(nested.CommandResult.Command.Name, out var create)) throw new ArgumentException("Batch supports read-only semantic queries only.");
		  // Use the outer solution, including an explicitly supplied path.
		  nested = Root.Parse(command + " --solution " + "\"" + path.Replace("\"", "\\\"") + "\"");
		  return create(nested);
	  }).ToArray();
			return ExecuteAsync(parse, RequestKind.MultiQuery, new MultiQueryRequest(path, queries, parse.GetValue(snapshot)), token);
		});
		Root.Subcommands.Add(batch);
	}
	private void AddServerCommands()
	{
		var server = new Command("server", "Manage the shared daemon.");
		foreach (string action in new[] { "status", "start", "stop", "ping" })
		{
			var command = new Command(action);
			command.SetAction(async (parse, token) =>
			{
				bool start = action == "start";
				var watch = System.Diagnostics.Stopwatch.StartNew();
				// Stop must also work against an incompatible daemon, so use a raw negotiated connection.
				await using Stream stream = await IpcClient.TryConnectAsync(Endpoint, token) ??
		(start ? await StartAndConnectAsync(token) : throw new IOException("Roslynk daemon is not running."));
				var client = new IpcClient(stream);
				ResponseEnvelope hello = await client.SendAsync(RequestKind.Hello, new HelloRequest(Wire.ProtocolVersion, Wire.ProductVersion), token);
				if (hello.Status != ResponseStatus.Success && action != "stop") throw new IOException(hello.Error?.Message ?? "Handshake failed.");
				RequestKind kind = action == "stop" ? RequestKind.Stop : action == "ping" ? RequestKind.Ping : RequestKind.ServerStatus;
				ResponseEnvelope response = await client.SendAsync(kind, new EmptyRequest(), token);
				if (response.Status != ResponseStatus.Success) throw new IOException(response.Error?.Message);
				ServerInfo info = Wire.Deserialize<ServerInfo>(response.Payload);
				await Output.WriteLineAsync(parse.GetValue(Json) ? JsonSerializer.Serialize(info) : $"{(action == "stop" ? "stopping" : "running")} pid={info.ProcessId} version={info.Version} uptime_ms={info.UptimeMilliseconds}");
				if (parse.GetValue(Timing)) await Error.WriteLineAsync($"total_ms={watch.Elapsed.TotalMilliseconds:F2}");
				if (action == "stop") await WaitForStopAsync(info.ProcessId, token);
				return 0;
			});
			server.Subcommands.Add(command);
		}
		Root.Subcommands.Add(server);
	}
	private async Task WaitForStopAsync(int processId, CancellationToken token)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
		timeout.CancelAfter(TimeSpan.FromSeconds(10));
		while (true)
		{
			try { using FileStream released = Endpoint.AcquireLock("daemon"); break; }
			catch (IOException) { await Task.Delay(20, timeout.Token); }
		}
		if (processId == Environment.ProcessId) return;
		try
		{
			using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(processId);
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (ArgumentException) { } // The process has already exited.
	}
	private async Task<Stream> StartAndConnectAsync(CancellationToken token)
	{
		await using IpcClient started = await IpcClient.ConnectAsync(Endpoint, autoStart: true, token);
		return await Endpoint.ConnectAsync(token);
	}
}
