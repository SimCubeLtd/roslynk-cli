using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;

namespace SimCube.Roslynk.Core.Features.Solutions.GetSolutionStatus;

internal sealed class GetSolutionStatusTool
{
	public const string GetSolutionStatusName = "get_solution_status";

	private readonly InstanceRegistry InstanceRegistry;

	public GetSolutionStatusTool(InstanceRegistry instanceRegistry)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
	}

	public string GetSolutionStatus()
	{
		List<RoslynInstance> instances = InstanceRegistry.LoadedInstances().ToList();

		var builder = new OutlineBuilder();
		builder.BeginBody();

		foreach (RoslynInstance instance in instances)
		{
			SolutionModel model = instance.CurrentModel;
			int? totalProjects = model.Solution?.Projects.Count();
			int loadedProjects = model.Status == SolutionStatus.Ready && totalProjects is int total
				? total
				: instance.LoadedProjects;

			string totalText = totalProjects?.ToString() ?? "?";
			builder.Line(0, $"{instance.Key.FilePath},{model.Status},{loadedProjects}/{totalText}");
		}

		return builder.ToString();
	}
}
