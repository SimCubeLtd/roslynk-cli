using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy;

internal sealed class GetTypeHierarchyTool
{
	public const string GetTypeHierarchyName = "get_type_hierarchy";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public GetTypeHierarchyTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> GetTypeHierarchy(
		string solutionId,
		string typeName, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await GetTypeHierarchyCoreAsync(model, instance, typeName, cancellationToken);
	}

	internal async Task<string> GetTypeHierarchyCoreAsync(SolutionModel model, RoslynInstance instance, string typeName, CancellationToken token = default)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups = await ProjectionService.ResolveAsync(SymbolResolver, projections, typeName);
		List<IReadOnlyList<ProjectionSymbol>> typeGroups = groups.Where(group => group[0].Symbol is INamedTypeSymbol).ToList();

		if (typeGroups.Count == 0)
		{
			IReadOnlyList<string> resolved = SymbolSignature.Distinguish(groups.Select(group => group[0].Symbol));
			IReadOnlyList<string> candidates = resolved.Count > 0
				? resolved
				: await SymbolResolver.SuggestAsync(model.Solution, typeName);

			return Failure(Error.NotFound($"No type matched '{typeName}'.", candidates.Count > 0 ? candidates : null));
		}

		if (typeGroups.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(typeName, typeGroups.Select(group => group[0].Symbol)));

		IReadOnlyList<ProjectionSymbol> resolvedType = typeGroups[0];
		var type = (INamedTypeSymbol)resolvedType[0].Symbol;

		var baseTypes = new List<INamedTypeSymbol>();
		for (INamedTypeSymbol? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
			baseTypes.Add(baseType);

		List<INamedTypeSymbol> interfaces = type.AllInterfaces
			.DistinctBy(SymbolResolver.FullyQualifiedName, StringComparer.Ordinal)
			.OrderBy(SymbolResolver.FullyQualifiedName, StringComparer.Ordinal)
			.ToList();

		// Union derived types across every projection so a derived type that compiles only in a branch
		// inactive in the loaded configuration is still listed.
		var seenDerived = new HashSet<string>(StringComparer.Ordinal);
		var derivedList = new List<INamedTypeSymbol>();
		foreach (ProjectionSymbol projectionSymbol in resolvedType)
		{
			foreach (INamedTypeSymbol derivedType in await SymbolFinder.FindDerivedClassesAsync((INamedTypeSymbol)projectionSymbol.Symbol, projectionSymbol.Projection.Solution))
			{
				if (seenDerived.Add(SymbolResolver.FullyQualifiedName(derivedType)))
					derivedList.Add(derivedType);
			}
		}

		List<INamedTypeSymbol> derived = derivedList
			.OrderBy(SymbolResolver.FullyQualifiedName, StringComparer.Ordinal)
			.ToList();

		var builder = new OutlineBuilder();
		builder.Header("resolvedType", SymbolResolver.SignatureName(type));
		builder.Status(model.Status);
		builder.BeginBody();

		Section(builder, "base", baseTypes);
		Section(builder, "interfaces", interfaces);
		Section(builder, "derived", derived);
		return builder.ToString();
	}

	private void Section(OutlineBuilder builder, string title, IReadOnlyList<INamedTypeSymbol> types)
	{
		if (types.Count == 0)
			return;

		builder.Line(0, title);
		foreach (INamedTypeSymbol type in types)
			builder.Line(1, $"{SymbolKindText.Of(type)},{OutlineBuilder.Field(SymbolResolver.FullyQualifiedName(type))}");
	}
}
