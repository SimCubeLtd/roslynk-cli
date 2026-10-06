using SimCube.Roslynk.Core.Infrastructure.Accesses;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.Core.Features.References.FindReads;

internal sealed class FindReadsTool
{
	public const string FindReadsName = "find_reads";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public FindReadsTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> FindReads(
		string solutionId,
		string symbolName,
		int maxResults = 100,
		CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await FindReadsCoreAsync(model, instance, symbolName, maxResults, cancellationToken);
	}

	internal Task<string> FindReadsCoreAsync(SolutionModel model, RoslynInstance instance, string symbolName, int maxResults = 100, CancellationToken cancellationToken = default) =>
		AccessQuery.RunAsync(model, SymbolResolver, ProjectionService, symbolName, maxResults, AccessKindText.IsRead, cancellationToken);
}
