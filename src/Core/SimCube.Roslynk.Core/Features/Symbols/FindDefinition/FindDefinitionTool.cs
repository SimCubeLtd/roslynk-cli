using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Symbols.FindDefinition;

internal sealed class FindDefinitionTool
{
	public const string FindDefinitionName = "find_definition";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public FindDefinitionTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> FindDefinition(
		string solutionId,
		string filePath,
		int line,
		int column, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await FindDefinitionCoreAsync(model, instance, filePath, line, column, cancellationToken);
	}

	internal async Task<string> FindDefinitionCoreAsync(SolutionModel model, RoslynInstance instance, string filePath, int line, int column, CancellationToken token = default)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		// Try each projection in turn: a position inside a branch inactive in the loaded configuration resolves
		// to no symbol in the base projection but binds in the projection where that branch is active.
		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		ISymbol? symbol = null;
		Solution resolvedSolution = model.Solution;
		foreach (Projection projection in projections)
		{
			symbol = await SymbolResolver.ResolveAtPositionAsync(projection.Solution, filePath, line, column);
			if (symbol is not null)
			{
				resolvedSolution = projection.Solution;
				break;
			}
		}

		if (symbol is null)
			return Failure(Error.NotFound($"No symbol resolved at {filePath} ({line}, {column})."));

		var builder = new OutlineBuilder();
		builder.Header("fullName", SymbolResolver.SignatureName(symbol));
		builder.Header("kind", SymbolKindText.Of(symbol));

		Location? location = symbol.Locations.FirstOrDefault(candidate => candidate.IsInSource);
		if (location is null)
		{
			if (symbol.ContainingAssembly is { } assembly)
				builder.Header("assembly", assembly.Name);

			return builder.ToString();
		}

		FileLinePositionSpan span = location.GetDisplaySpan();
		if (ProjectName.Of(resolvedSolution, location.SourceTree!) is string project)
			builder.Header("project", project);
		builder.Header("path", SolutionRelativePath.Of(solutionDirectory, span.Path)!);
		builder.Header("loc", $"{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}-{span.EndLinePosition.Line + 1}:{span.EndLinePosition.Character + 1}");
		return builder.ToString();
	}
}
