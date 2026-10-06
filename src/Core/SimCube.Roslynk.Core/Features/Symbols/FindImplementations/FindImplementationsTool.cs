using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Symbols.FindImplementations;

internal sealed class FindImplementationsTool
{
	public const string FindImplementationsName = "find_implementations";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public FindImplementationsTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> FindImplementations(
		string solutionId,
		string symbolName, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await FindImplementationsCoreAsync(model, instance, symbolName, cancellationToken);
	}

	internal async Task<string> FindImplementationsCoreAsync(SolutionModel model, RoslynInstance instance, string symbolName, CancellationToken token = default)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups = await ProjectionService.ResolveAsync(SymbolResolver, projections, symbolName);
		if (groups.Count == 0)
			return Failure(Error.NotFound($"No symbol matched '{symbolName}'."));
		if (groups.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(symbolName, groups.Select(group => group[0].Symbol)));

		IReadOnlyList<ProjectionSymbol> resolved = groups[0];

		// Union implementors across every projection (per-TFM + #if-toggle), deduped by stable symbol identity,
		// so implementors declared in a branch that is inactive in the loaded configuration are still listed.
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var root = new SymbolNode();
		foreach (ProjectionSymbol projectionSymbol in resolved)
		{
			foreach (ISymbol implementation in await SymbolFinder.FindImplementationsAsync(projectionSymbol.Symbol, projectionSymbol.Projection.Solution))
			{
				if (seen.Add(ProjectionService.KeyOf(implementation)))
					SymbolPlacement.Place(root, implementation, projectionSymbol.Projection.Solution, solutionDirectory);
			}
		}

		var builder = new OutlineBuilder();
		builder.Header("resolvedSymbol", SymbolResolver.SignatureName(resolved[0].Symbol));
		builder.Status(model.Status);
		builder.BeginBody();
		root.Render(builder);
		return builder.ToString();
	}
}
