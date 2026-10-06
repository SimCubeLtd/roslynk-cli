using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics;

internal sealed class GetDiagnosticsTool
{
	public const string GetDiagnosticsName = "get_diagnostics";

	private const string NoLocationBucket = "<no-location>";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly DiagnosticsService DiagnosticsService;

	public GetDiagnosticsTool(InstanceRegistry instanceRegistry, DiagnosticsService diagnosticsService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		DiagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
	}

	public async Task<string> GetDiagnostics(
		string solutionId,
		bool includeErrors = false,
		bool includeWarnings = false,
		bool includeInfo = false,
		bool includeHidden = false,
		bool includeAnalyzers = true, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);

		if (model.Solution is null)
			return OutlineError.Format(Error.Indexing(), model.Status);

		// Fence writes, drain in-flight ones, then build (or reuse the cached result when nothing changed).
		string cacheKey = $"{includeAnalyzers}";
		DiagnosticsResult diagnostics = await instance.RequestDiagnosticsAsync(
			cacheKey,
			(solution, token) => DiagnosticsService.GetAllDiagnosticsAsync(solution, includeAnalyzers, token), cancellationToken);

		Solution compiled = diagnostics.Solution;
		string? solutionDirectory = SolutionRelativePath.DirectoryOf(compiled);

		// Analyzers report private trigger ids alongside the public rule (IDE0005's, for one). They have no
		// message and nothing to act on, so they are dropped before the counts as well as the body.
		List<Diagnostic> all = diagnostics.Diagnostics
			.Where(diagnostic => !CodeActionCatalog.IsPrivateFixTrigger(diagnostic.Id))
			.ToList();

		var wanted = new HashSet<DiagnosticSeverity>();
		if (includeErrors)
			wanted.Add(DiagnosticSeverity.Error);
		if (includeWarnings)
			wanted.Add(DiagnosticSeverity.Warning);
		if (includeInfo)
			wanted.Add(DiagnosticSeverity.Info);
		if (includeHidden)
			wanted.Add(DiagnosticSeverity.Hidden);

		List<Diagnostic> items = all.Where(diagnostic => wanted.Contains(diagnostic.Severity)).ToList();

		var builder = new OutlineBuilder();
		builder.Header("errors", all.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
		builder.Header("warnings", all.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning));
		builder.Header("infos", all.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Info));
		builder.Header("hidden", all.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Hidden));
		builder.Status(instance.CurrentModel.Status);
		builder.BeginBody();

		IEnumerable<IGrouping<string?, Diagnostic>> byProject = items
			.GroupBy(diagnostic => ProjectOf(diagnostic, compiled))
			.OrderBy(group => group.Key is null)
			.ThenBy(group => group.Key, StringComparer.Ordinal);

		foreach (IGrouping<string?, Diagnostic> project in byProject)
		{
			int fileDepth = 0;
			if (project.Key is string projectName)
			{
				builder.Line(0, projectName);
				fileDepth = 1;
			}

			FolderFiles.Write(builder, fileDepth, project, diagnostic => FileOf(diagnostic, solutionDirectory), (severityDepth, file) =>
			{
				IEnumerable<IGrouping<DiagnosticSeverity, Diagnostic>> bySeverity = file
					.GroupBy(diagnostic => diagnostic.Severity)
					.OrderByDescending(group => group.Key);

				foreach (IGrouping<DiagnosticSeverity, Diagnostic> severity in bySeverity)
				{
					builder.Line(severityDepth, SeverityLabel(severity.Key));

					IEnumerable<Diagnostic> ordered = severity
						.OrderBy(diagnostic => diagnostic.Location.IsInSource ? 0 : 1)
						.ThenBy(diagnostic => diagnostic.Location.IsInSource
							? diagnostic.Location.GetDisplaySpan().StartLinePosition.Line
							: int.MaxValue)
						.ThenBy(diagnostic => diagnostic.Location.IsInSource
							? diagnostic.Location.GetDisplaySpan().StartLinePosition.Character
							: int.MaxValue);

					foreach (Diagnostic diagnostic in ordered)
						builder.Line(severityDepth + 1, EntryText(diagnostic));
				}
			});
		}

		return builder.ToString();
	}

	private static string? ProjectOf(Diagnostic diagnostic, Solution solution) =>
		diagnostic.Location.SourceTree is SyntaxTree tree ? ProjectName.Of(solution, tree) : null;

	private static string FileOf(Diagnostic diagnostic, string? solutionDirectory) =>
		diagnostic.Location.IsInSource
			? SolutionRelativePath.Of(solutionDirectory, diagnostic.Location.GetDisplaySpan().Path)!
			: NoLocationBucket;

	private static string SeverityLabel(DiagnosticSeverity severity) =>
		severity switch {
			DiagnosticSeverity.Error => "errors",
			DiagnosticSeverity.Warning => "warnings",
			DiagnosticSeverity.Info => "infos",
			_ => "hidden",
		};

	private static string EntryText(Diagnostic diagnostic)
	{
		string message = OutlineBuilder.Sanitize(diagnostic.GetMessage());

		if (!diagnostic.Location.IsInSource)
			return $"{diagnostic.Id},{message}";

		FileLinePositionSpan span = diagnostic.Location.GetDisplaySpan();
		int line = span.StartLinePosition.Line + 1;
		int column = span.StartLinePosition.Character + 1;
		return $"{diagnostic.Id},{line}:{column},{message}";
	}
}
