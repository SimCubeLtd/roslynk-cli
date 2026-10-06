using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Symbols.SearchSymbols;

internal sealed class SearchSymbolsTool
{
	public const string SearchSymbolsName = "search_symbols";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly ProjectionService ProjectionService;

	public SearchSymbolsTool(InstanceRegistry instanceRegistry, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> SearchSymbols(
		string solutionId,
		string query,
		int maxResults = 50, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await SearchSymbolsCoreAsync(model, instance, query, maxResults, cancellationToken);
	}

	internal async Task<string> SearchSymbolsCoreAsync(SolutionModel model, RoslynInstance instance, string query, int maxResults = 50, CancellationToken token = default)
	{
		if (model.Solution is null)
			return OutlineError.Format(Error.Indexing(), model.Status);

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		// Search every projection so declarations that compile only in a branch inactive in the loaded
		// configuration are included; dedupe by fully-qualified name across projections.
		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var matched = new List<(ISymbol Symbol, Solution Solution)>();
		foreach (Projection projection in projections)
		{
			foreach (ISymbol symbol in await SymbolFinder.FindSourceDeclarationsAsync(projection.Solution, name => name.Contains(query, StringComparison.OrdinalIgnoreCase)))
			{
				if (seen.Add(SymbolSignature.Of(symbol, SignatureTier.FullyQualifiedWithRefKinds)))
					matched.Add((symbol, projection.Solution));
			}

			// Local functions are not in the declaration index, so they are found by walking the syntax.
			foreach (IMethodSymbol local in await LocalFunctions.FindAllAsync(projection.Solution, name => name.Contains(query, StringComparison.OrdinalIgnoreCase), token))
			{
				if (seen.Add(SymbolSignature.Of(local, SignatureTier.FullyQualifiedWithRefKinds)))
					matched.Add((local, projection.Solution));
			}
		}

		List<(ISymbol Symbol, Solution Solution)> results = matched.Take(Math.Max(0, maxResults)).ToList();

		var root = new SymbolNode();
		foreach ((ISymbol symbol, Solution symbolSolution) in results)
			SymbolPlacement.Place(root, symbol, symbolSolution, solutionDirectory);

		var builder = new OutlineBuilder();
		if (matched.Count > results.Count)
		{
			builder.Header("count", matched.Count);
			builder.Header("truncated", true);
		}
		builder.Status(model.Status);
		builder.BeginBody();
		root.Render(builder);
		return builder.ToString();
	}
}
