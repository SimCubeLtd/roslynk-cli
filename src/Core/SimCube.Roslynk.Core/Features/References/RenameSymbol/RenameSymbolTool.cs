using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Renaming;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.Core.Features.References.RenameSymbol;

internal sealed class RenameSymbolTool
{
	public const string RenameSymbolName = "rename_symbol";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;
	private readonly ApplyPipeline ApplyPipeline;

	public RenameSymbolTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService, ApplyPipeline applyPipeline)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
		ApplyPipeline = applyPipeline ?? throw new ArgumentNullException(nameof(applyPipeline));
	}

	public async Task<string> RenameSymbol(
		string solutionId,
		string symbolName,
		string newName,
		bool checkOnly = false,
		CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = instance.CurrentModel;

		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (!SyntaxFacts.IsValidIdentifier(newName))
			return Failure(Error.Invalid($"'{newName}' is not a valid C# identifier."));

		if (model.Solution is null)
			return Failure(Error.Indexing());

		Solution baseSolution = model.Solution;
		string? solutionDirectory = SolutionRelativePath.DirectoryOf(baseSolution);

		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(baseSolution, cancellationToken);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups = await ProjectionService.ResolveAsync(SymbolResolver, projections, symbolName, cancellationToken);
		if (groups.Count == 0)
		{
			IReadOnlyList<string> suggestions = await SymbolResolver.SuggestAsync(baseSolution, symbolName, cancellationToken: cancellationToken);
			return Failure(Error.NotFound($"No symbol matched '{symbolName}'.", suggestions.Count > 0 ? suggestions : null));
		}
		if (groups.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(symbolName, groups.Select(group => group[0].Symbol)));

		IReadOnlyList<ProjectionSymbol> resolved = groups[0];
		string resolvedName = SymbolResolver.SignatureName(resolved[0].Symbol);

		Solution updated;
		try
		{
			updated = await ProjectionRenamer.RenameAsync(
				baseSolution,
				resolved.Select(projectionSymbol => new RenameTarget(projectionSymbol.Projection.Solution, projectionSymbol.Symbol)),
				newName,
				cancellationToken);
		}
		catch (RazorMappingException exception)
		{
			return Failure(exception.Kind == RazorMappingFailure.TextMismatch
				? Error.Conflict(exception.Message)
				: Error.NotSupported(exception.Message));
		}
		catch (RenameConflictException exception)
		{
			return Failure(Error.Conflict(exception.Message));
		}

		IReadOnlyList<string> changed;
		if (checkOnly)
		{
			changed = ApplyPipeline.GetChangedFilePaths(baseSolution, updated);
		}
		else
		{
			try
			{
				changed = await ApplyPipeline.ApplyAsync(instance, updated, basedOn: baseSolution, cancellationToken);
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
		builder.Header("resolvedSymbol", resolvedName);
		builder.Status(instance.CurrentModel.Status);
		ChangedFilesOutline.Write(builder, changed, instance.CurrentSolution, solutionDirectory);
		return builder.ToString();
	}
}
