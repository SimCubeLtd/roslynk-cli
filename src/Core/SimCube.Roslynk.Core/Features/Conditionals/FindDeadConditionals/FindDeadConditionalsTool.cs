using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Conditionals.FindDeadConditionals;

internal sealed class FindDeadConditionalsTool
{
	public const string FindDeadConditionalsName = "find_dead_conditionals";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly ConditionalCoverage ConditionalCoverage;

	public FindDeadConditionalsTool(InstanceRegistry instanceRegistry, ConditionalCoverage conditionalCoverage)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		ConditionalCoverage = conditionalCoverage ?? throw new ArgumentNullException(nameof(conditionalCoverage));
	}

	public async Task<string> FindDeadConditionals(
		string solutionId, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await FindDeadConditionalsCoreAsync(model, instance, cancellationToken);
	}

	internal async Task<string> FindDeadConditionalsCoreAsync(SolutionModel model, RoslynInstance instance, CancellationToken token = default)
	{
		if (model.Solution is null)
			return OutlineError.Format(Error.Indexing(), model.Status);

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);
		IReadOnlyList<DeadConditional> dead = await ConditionalCoverage.FindNeverBuiltAsync(model.Solution);

		var builder = new OutlineBuilder();
		builder.Header("deadConditionals", dead.Count);
		builder.Status(model.Status);
		builder.BeginBody();

		IEnumerable<IGrouping<string, DeadConditional>> byFile = dead
			.GroupBy(item => SolutionRelativePath.Of(solutionDirectory, item.FilePath) ?? item.FilePath)
			.OrderBy(group => group.Key, StringComparer.Ordinal);

		foreach (IGrouping<string, DeadConditional> file in byFile)
		{
			builder.Line(0, file.Key);
			foreach (DeadConditional item in file.OrderBy(entry => entry.Line).ThenBy(entry => entry.Column))
				builder.Line(1, $"{item.Line}:{item.Column},{item.Directive},{OutlineBuilder.Field(item.Condition)}");
		}

		return builder.ToString();
	}
}
