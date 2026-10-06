using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction;

internal sealed class ApplyCodeActionTool
{
	public const string ApplyCodeActionName = "apply_code_action";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly CodeActionService CodeActionService;
	private readonly ApplyPipeline ApplyPipeline;

	public ApplyCodeActionTool(InstanceRegistry instanceRegistry, CodeActionService codeActionService, ApplyPipeline applyPipeline)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		CodeActionService = codeActionService ?? throw new ArgumentNullException(nameof(codeActionService));
		ApplyPipeline = applyPipeline ?? throw new ArgumentNullException(nameof(applyPipeline));
	}

	public async Task<string> ApplyCodeAction(
		string solutionId,
		string actionId,
		bool checkOnly = false,
		CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = instance.CurrentModel;

		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		if (!CodeActionService.TryDecodeId(actionId, out ActionRef actionRef))
			return Failure(Error.Invalid("The actionId could not be decoded; get a fresh one from get_code_actions."));

		Solution solution = model.Solution;
		string? solutionDirectory = SolutionRelativePath.DirectoryOf(solution);

		RazorSourceDocument? source = await RazorSourceDocument.ResolveAsync(solution, actionRef.DocumentPath, cancellationToken);
		if (source is null)
			return Failure(Error.NotFound($"'{actionRef.DocumentPath}' is no longer a solution document."));

		Solution? changed = await CodeActionService.ComputeChangedSolutionAsync(source.Document, actionRef, cancellationToken);
		if (changed is null)
			return Failure(Error.Conflict("The action is no longer available; the code may have changed. Re-run get_code_actions."));

		try
		{
			changed = await RazorGeneratedChangeFolder.FoldAsync(solution, changed, cancellationToken);
		}
		catch (RazorMappingException exception)
		{
			return Failure(RazorGeneratedChangeFolder.ErrorFor(exception));
		}

		IReadOnlyList<string> files;
		if (checkOnly)
		{
			files = ApplyPipeline.GetChangedFilePaths(solution, changed);
		}
		else
		{
			try
			{
				files = await ApplyPipeline.ApplyAsync(instance, changed, basedOn: solution, cancellationToken);
			}
			catch (StaleWriteException exception)
			{
				return OutlineError.Format(
					Error.Stale(exception.Message, [SolutionRelativePath.Of(solutionDirectory, exception.FilePath) ?? exception.FilePath]),
					instance.CurrentModel.Status);
			}
		}

		var builder = new OutlineBuilder();
		builder.Header("applied", !checkOnly);
		builder.Header("action", actionRef.Key);
		builder.Status(instance.CurrentModel.Status);
		ChangedFilesOutline.Write(builder, files, instance.CurrentSolution, solutionDirectory);
		return builder.ToString();
	}
}
