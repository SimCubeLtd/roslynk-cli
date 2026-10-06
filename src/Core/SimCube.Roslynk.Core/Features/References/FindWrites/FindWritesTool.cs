using SimCube.Roslynk.Core.Infrastructure.Accesses;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.Core.Features.References.FindWrites;

internal sealed class FindWritesTool
{
	public const string FindWritesName = "find_writes";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public FindWritesTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> FindWrites(
		string solutionId,
		string symbolName,
		int maxResults = 100,
		CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await FindWritesCoreAsync(model, instance, symbolName, maxResults, cancellationToken);
	}

	internal Task<string> FindWritesCoreAsync(SolutionModel model, RoslynInstance instance, string symbolName, int maxResults = 100, CancellationToken cancellationToken = default) =>
		AccessQuery.RunAsync(model, SymbolResolver, ProjectionService, symbolName, maxResults, AccessKindText.IsWrite, cancellationToken);
}
