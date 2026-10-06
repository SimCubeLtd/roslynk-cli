using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SimCube.Roslynk.Core.Infrastructure.Decompilation;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody;

internal sealed class GetSymbolBodyTool
{
	public const string GetSymbolBodyName = "get_symbol_body";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public GetSymbolBodyTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> GetSymbolBody(
		string solutionId,
		string symbolName,
		bool includeLeadingTrivia = false,
		bool decompile = false,
		CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await RunAsync(model, instance, symbolName, includeLeadingTrivia, decompile, cancellationToken);
	}

	/// <summary>
	/// The pinned-snapshot entry point used by batches. It deliberately has no decompile parameter: decompiling
	/// is an explicit single-command request, so a batch can never trigger it.
	/// </summary>
	internal Task<string> GetSymbolBodyCoreAsync(SolutionModel model, RoslynInstance instance, string symbolName, bool includeLeadingTrivia = false, CancellationToken cancellationToken = default) =>
		RunAsync(model, instance, symbolName, includeLeadingTrivia, decompile: false, cancellationToken);

	private async Task<string> RunAsync(SolutionModel model, RoslynInstance instance, string symbolName, bool includeLeadingTrivia, bool decompile, CancellationToken cancellationToken)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		// Resolve across every projection so a symbol declared only in a branch inactive in the loaded
		// configuration is still found; grouping is signature-aware, so overloads stay separate groups.
		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution, cancellationToken);
		IReadOnlyList<IReadOnlyList<ProjectionSymbol>> groups =
			await ProjectionService.ResolveAsync(SymbolResolver, projections, symbolName, cancellationToken);

		if (groups.Count == 0)
		{
			// Nothing in source. A name that still resolves against a referenced assembly is a real symbol with
			// no source body, which is NotSupported rather than NotFound unless decompilation was asked for.
			IReadOnlyList<ISymbol> metadata =
				await SymbolResolver.FindByFullyQualifiedNameWithMetadataAsync(model.Solution, symbolName, cancellationToken);
			if (metadata.Count > 1 && decompile)
				return Failure(SymbolAmbiguity.Ambiguous(symbolName, metadata));

			if (metadata.Count > 0 && decompile)
				return await DecompileAsync(model, instance, symbolName, metadata[0], cancellationToken);

			if (metadata.Count > 0)
			{
				string assembly = metadata[0].ContainingAssembly is { } owner ? $" It comes from '{owner.Name}'." : "";
				return Failure(Error.NotSupported(
					$"'{symbolName}' has no source declaration, so it has no body to return.{assembly} Use get_symbol for its signature, or request decompilation (body --decompile) to read reconstructed source."));
			}

			IReadOnlyList<string> suggestions = await SymbolResolver.SuggestAsync(model.Solution, symbolName);
			return Failure(Error.NotFound($"No symbol matched '{symbolName}'.", suggestions.Count > 0 ? suggestions : null));
		}

		if (groups.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(symbolName, groups.Select(group => group[0].Symbol)));

		ProjectionSymbol resolved = groups[0][0];
		ISymbol symbol = resolved.Symbol;

		if (symbol.DeclaringSyntaxReferences.Length == 0)
		{
			string where = symbol.ContainingAssembly is { } assembly ? $" It comes from '{assembly.Name}'." : "";
			return Failure(Error.NotSupported(
				$"'{symbolName}' has no source declaration, so it has no body to return.{where} Use get_symbol for its signature."));
		}

		var parts = new List<Part>();
		foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
		{
			Part? part = await BuildPartAsync(reference, resolved.Projection.Solution, solutionDirectory, includeLeadingTrivia, cancellationToken);
			if (part is not null)
				parts.Add(part);
		}

		if (parts.Count == 0)
			return Failure(Error.NotSupported($"'{symbolName}' has no readable source declaration."));

		return parts.Count == 1 ? Single(parts[0]) : Multiple(parts);
	}

	/// <summary>
	/// Reconstructs a metadata symbol's declaration from the IL of the assembly the solution references. The
	/// result is labelled <c>source=decompiled</c> and names the file it was read from; it has no project,
	/// path or location because it is not source in the solution.
	/// </summary>
	private static async Task<string> DecompileAsync(SolutionModel model, RoslynInstance instance, string symbolName, ISymbol symbol, CancellationToken cancellationToken)
	{
		string Failure(string reason) => OutlineError.Format(Error.NotSupported($"'{symbolName}' cannot be decompiled. {reason}"), model.Status);

		ISymbol definition = symbol.OriginalDefinition;
		if (definition.GetDocumentationCommentId() is not { } documentationId || definition.ContainingAssembly is not { } assembly)
			return Failure("Only types, methods, properties, fields and events can be decompiled.");

		// Only the compilation that produced the symbol can map its assembly back to a file on disk.
		PortableExecutableReference? reference = null;
		foreach (Project project in model.Solution!.Projects)
		{
			Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
			reference = compilation?.GetMetadataReference(assembly) as PortableExecutableReference;
			if (reference?.FilePath is not null)
				break;
		}

		if (reference?.FilePath is not { } assemblyPath || !File.Exists(assemblyPath))
			return Failure($"The file for '{assembly.Name}' was not found.");

		// The assembly's own dependencies are resolved from wherever this solution references them.
		string[] searchDirectories = model.Solution.Projects
			.SelectMany(project => project.MetadataReferences)
			.OfType<PortableExecutableReference>()
			.Select(candidate => Path.GetDirectoryName(candidate.FilePath))
			.OfType<string>()
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		DecompiledSource decompiled;
		try
		{
			decompiled = await Task.Run(() => instance.Decompiler.Decompile(assemblyPath, documentationId, searchDirectories, cancellationToken), cancellationToken);
		}
		catch (DecompilationException exception)
		{
			return Failure(exception.Message);
		}

		var builder = new OutlineBuilder();
		builder.Header("source", "decompiled");
		builder.Header("assembly", decompiled.AssemblyName);
		builder.Header("file", decompiled.AssemblyPath);
		builder.BeginBody();
		builder.Line(0, decompiled.Text);
		return builder.ToString();
	}

	private static string Single(Part part)
	{
		var builder = new OutlineBuilder();
		if (part.Project is not null)
			builder.Header("project", part.Project);
		builder.Header("path", part.Path);
		builder.Header("loc", part.Location);
		builder.BeginBody();
		builder.Line(0, part.Text);
		return builder.ToString();
	}

	private static string Multiple(IReadOnlyList<Part> parts)
	{
		var builder = new OutlineBuilder();
		builder.Header("parts", parts.Count);
		builder.BeginBody();

		for (int index = 0; index < parts.Count; index++)
		{
			Part part = parts[index];
			if (index > 0)
				builder.Line(0, "");

			string project = part.Project is null ? "" : $"project={OutlineBuilder.Field(part.Project)},";
			builder.Line(0, $"part={index + 1},{project}path={part.Path},loc={part.Location}");
			builder.Line(0, part.Text);
		}

		return builder.ToString();
	}

	private static async Task<Part?> BuildPartAsync(
		SyntaxReference reference,
		Solution solution,
		string? solutionDirectory,
		bool includeLeadingTrivia,
		CancellationToken cancellationToken)
	{
		SyntaxNode node = await reference.GetSyntaxAsync(cancellationToken);

		// A field/event symbol declares a VariableDeclaratorSyntax; the declaration a caller wants to read is
		// the whole field declaration (modifiers, type and initializer) that owns it.
		if (node is VariableDeclaratorSyntax variable && variable.FirstAncestorOrSelf<BaseFieldDeclarationSyntax>() is { } field)
			node = field;

		TextSpan span = includeLeadingTrivia ? WithLeadingTrivia(node) : node.Span;
		SourceText text = await reference.SyntaxTree.GetTextAsync(cancellationToken);
		FileLinePositionSpan display = reference.SyntaxTree.GetDisplaySpan(span);

		string? path = SolutionRelativePath.Of(solutionDirectory, display.Path);
		if (path is null)
			return null;

		return new Part(
			ProjectName.Of(solution, reference.SyntaxTree),
			path,
			$"{display.StartLinePosition.Line + 1}:{display.StartLinePosition.Character + 1}-{display.EndLinePosition.Line + 1}:{display.EndLinePosition.Character + 1}",
			text.ToString(span));
	}

	/// <summary>
	/// The node's span extended back over its leading comments, XML documentation and directives. The blank
	/// lines and indentation that separate the declaration from whatever precedes it are excluded, so the
	/// result starts at the first line that actually belongs to the declaration.
	/// </summary>
	private static TextSpan WithLeadingTrivia(SyntaxNode node)
	{
		SyntaxTriviaList trivia = node.GetLeadingTrivia();
		foreach (SyntaxTrivia candidate in trivia)
		{
			if (candidate.IsKind(SyntaxKind.WhitespaceTrivia)
				|| candidate.IsKind(SyntaxKind.EndOfLineTrivia))
			{
				continue;
			}

			return TextSpan.FromBounds(candidate.FullSpan.Start, node.Span.End);
		}

		return node.Span;
	}

	private sealed record Part(string? Project, string Path, string Location, string Text);
}
