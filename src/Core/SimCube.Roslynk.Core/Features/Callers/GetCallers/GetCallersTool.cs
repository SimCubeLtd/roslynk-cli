using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Callers.GetCallers;

internal sealed class GetCallersTool
{
	public const string GetCallersName = "get_callers";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public GetCallersTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> GetCallers(
		string solutionId,
		string methodName, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await GetCallersCoreAsync(model, instance, methodName, cancellationToken);
	}

	internal async Task<string> GetCallersCoreAsync(SolutionModel model, RoslynInstance instance, string methodName, CancellationToken token = default)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups = await ProjectionService.ResolveAsync(SymbolResolver, projections, methodName);

		if (groups.Count == 0)
		{
			IReadOnlyList<string> candidates = await SymbolResolver.SuggestAsync(model.Solution, methodName);
			return Failure(Error.NotFound($"No symbol matched '{methodName}'.", candidates.Count > 0 ? candidates : null));
		}

		if (groups.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(methodName, groups.Select(group => group[0].Symbol)));

		IReadOnlyList<ProjectionSymbol> resolved = groups[0];

		// Union callers across every projection, deduped by stable symbol identity, so a caller that only
		// compiles in a branch inactive in the loaded configuration is still reported.
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var root = new SymbolNode();
		foreach (ProjectionSymbol projectionSymbol in resolved)
		{
			foreach (SymbolCallerInfo caller in await SymbolFinder.FindCallersAsync(projectionSymbol.Symbol, projectionSymbol.Projection.Solution))
			{
				if (seen.Add(ProjectionService.KeyOf(caller.CallingSymbol)))
					SymbolPlacement.Place(root, caller.CallingSymbol, projectionSymbol.Projection.Solution, solutionDirectory);
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
