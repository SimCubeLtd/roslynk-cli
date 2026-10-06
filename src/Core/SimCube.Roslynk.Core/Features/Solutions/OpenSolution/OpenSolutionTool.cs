using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Solutions.OpenSolution;

internal sealed class OpenSolutionTool
{
	public const string OpenSolutionName = "open_solution";

	private readonly InstanceRegistry InstanceRegistry;

	public OpenSolutionTool(InstanceRegistry instanceRegistry)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
	}

	public string OpenSolution(
		string solutionPath)
	{
		string fullPath = Path.GetFullPath(solutionPath);
		if (!File.Exists(fullPath))
			return OutlineError.Format(
				Error.NotFound($"No solution file was found at '{fullPath}'."),
				SolutionStatus.Faulted);

		RoslynInstance instance = InstanceRegistry.GetOrBegin(solutionPath);
		SolutionModel model = instance.CurrentModel;

		if (model.Status == SolutionStatus.Faulted)
			return OutlineError.Format(Error.Faulted(model.FaultMessage ?? "The solution failed to load."), model.Status);

		string? solutionDirectory = model.Solution is null
			? Path.GetDirectoryName(instance.Key.FilePath)
			: SolutionRelativePath.DirectoryOf(model.Solution);
		int loadDiagnostics = instance.Workspace?.LoadDiagnostics.Count ?? 0;
		List<Project> projects = model.Solution?.Projects.ToList() ?? [];

		var builder = new OutlineBuilder();
		builder.Header("solutionId", instance.Key.FilePath);
		builder.Status(model.Status);
		builder.Header("projects", projects.Count);
		builder.Header("loadDiagnostics", loadDiagnostics);
		builder.BeginBody();

		foreach (Project project in projects)
		{
			string path = SolutionRelativePath.Of(solutionDirectory, project.FilePath) ?? project.Name;
			builder.Line(0, $"{path},{project.Documents.Count()}");
		}

		return builder.ToString();
	}
}
