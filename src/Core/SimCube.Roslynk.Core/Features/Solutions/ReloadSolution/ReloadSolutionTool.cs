using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.Core.Features.Solutions.ReloadSolution;

internal sealed class ReloadSolutionTool
{
	public const string ReloadSolutionName = "reload_solution";

	private readonly InstanceRegistry InstanceRegistry;

	public ReloadSolutionTool(InstanceRegistry instanceRegistry)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
	}

	public string ReloadSolution(
		string solutionId)
	{
		RoslynInstance instance = InstanceRegistry.BeginReload(solutionId);
		SolutionModel model = instance.CurrentModel;

		if (model.Status == SolutionStatus.Faulted)
			return OutlineError.Format(Error.Faulted(model.FaultMessage ?? "The reload failed."), model.Status);

		return new OutlineBuilder()
			.Header("solutionId", instance.Key.FilePath)
			.Status(model.Status)
			.Header("projects", model.Solution?.Projects.Count() ?? 0)
			.ToString();
	}
}
