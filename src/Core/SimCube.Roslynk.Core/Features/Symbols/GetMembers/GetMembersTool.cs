using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Razor;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.Core.Features.Symbols.GetMembers;

internal sealed class GetMembersTool
{
	public const string GetMembersName = "get_members";

	private const string MetadataBucket = "<metadata>";

	private readonly InstanceRegistry InstanceRegistry;
	private readonly SymbolResolver SymbolResolver;
	private readonly ProjectionService ProjectionService;

	public GetMembersTool(InstanceRegistry instanceRegistry, SymbolResolver symbolResolver, ProjectionService projectionService)
	{
		InstanceRegistry = instanceRegistry ?? throw new ArgumentNullException(nameof(instanceRegistry));
		SymbolResolver = symbolResolver ?? throw new ArgumentNullException(nameof(symbolResolver));
		ProjectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
	}

	public async Task<string> GetMembers(
		string solutionId,
		string typeName,
		bool includeInherited = false,
		string? nameFilter = null,
		bool includeMethods = true,
		bool includeFields = true,
		bool includeProperties = true,
		bool includeEvents = true,
		bool includeNestedTypes = true, CancellationToken cancellationToken = default)
	{
		RoslynInstance instance = await InstanceRegistry.GetOrBeginAsync(solutionId);
		SolutionModel model = await instance.ReadModelAsync(cancellationToken);
		return await GetMembersCoreAsync(model, instance, typeName, includeInherited, nameFilter, includeMethods, includeFields, includeProperties, includeEvents, includeNestedTypes, cancellationToken);
	}

	internal async Task<string> GetMembersCoreAsync(
		SolutionModel model,
		RoslynInstance instance,
		string typeName,
		bool includeInherited = false,
		string? nameFilter = null,
		bool includeMethods = true,
		bool includeFields = true,
		bool includeProperties = true,
		bool includeEvents = true,
		bool includeNestedTypes = true,
		CancellationToken token = default)
	{
		string Failure(Error error) => OutlineError.Format(error, model.Status);

		if (model.Solution is null)
			return Failure(Error.Indexing());

		string? solutionDirectory = SolutionRelativePath.DirectoryOf(model.Solution);

		// Resolve the type in every projection so members declared in a branch inactive in the loaded
		// configuration are included; group by fully-qualified name so the same type across projections is one.
		IReadOnlyList<Projection> projections = await ProjectionService.BuildAsync(model.Solution);
		var typeInstances = new List<(INamedTypeSymbol Type, Solution Solution)>();
		// Keyed by signature and holding the symbol, so an ambiguous match can emit candidates that are
		// distinguishable from each other and accepted back verbatim.
		var distinctTypes = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
		foreach (Projection projection in projections)
		{
			foreach (INamedTypeSymbol candidate in (await SymbolResolver.FindByFullyQualifiedNameWithMetadataAsync(projection.Solution, typeName)).OfType<INamedTypeSymbol>())
			{
				typeInstances.Add((candidate, projection.Solution));
				distinctTypes.TryAdd(ProjectionService.KeyOf(candidate), candidate);
			}
		}

		if (distinctTypes.Count == 0)
			return Failure(Error.NotFound($"No type matched '{typeName}'."));
		if (distinctTypes.Count > 1)
			return Failure(SymbolAmbiguity.Ambiguous(typeName, distinctTypes.Values));

		INamedTypeSymbol type = typeInstances[0].Type;

		bool NameMatches(string name)
		{
			if (string.IsNullOrEmpty(nameFilter))
				return true;
			if (nameFilter.EndsWith('*'))
				return name.StartsWith(nameFilter[..^1], StringComparison.OrdinalIgnoreCase);

			return name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase);
		}

		bool KindIncluded(ISymbol member) =>
			member.Kind switch {
				SymbolKind.Method => includeMethods,
				SymbolKind.Field => includeFields,
				SymbolKind.Property => includeProperties,
				SymbolKind.Event => includeEvents,
				SymbolKind.NamedType => includeNestedTypes,
				_ => true,
			};

		// Union members across every projection instance of the type, deduped by stable identity so a member
		// in shared code is listed once while a member declared only in an inactive branch still appears.
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var entries = new List<(string? Project, string File, int Order, string Line, IReadOnlyList<(int Depth, string Line)> LocalFunctions)>();
		foreach ((INamedTypeSymbol typeInstance, Solution typeSolution) in typeInstances)
		{
			foreach (ISymbol member in Collect(typeInstance, includeInherited)
				.Where(member => !member.IsImplicitlyDeclared)
				.Where(KindIncluded)
				.Where(member => NameMatches(member.Name)))
			{
				if (!seen.Add(ProjectionService.KeyOf(member)))
					continue;

				(string? project, string file, int order, string line) = Render(member, typeSolution, solutionDirectory);
				var localFunctions = new List<(int Depth, string Line)>();
				await AddLocalFunctionsAsync(localFunctions, member, depth: 1, typeSolution, solutionDirectory, token);
				entries.Add((project, file, order, line, localFunctions));
			}
		}

		var builder = new OutlineBuilder();
		builder.Header("resolvedType", SymbolResolver.SignatureName(type));
		builder.Status(model.Status);
		builder.BeginBody();

		var byProject = entries
			.GroupBy(entry => entry.Project)
			.OrderBy(group => group.Key is null)
			.ThenBy(group => group.Key, StringComparer.Ordinal);

		foreach (var project in byProject)
		{
			int fileDepth = 0;
			if (project.Key is string projectName)
			{
				builder.Line(0, projectName);
				fileDepth = 1;
			}

			FolderFiles.Write(builder, fileDepth, project, entry => entry.File, (memberDepth, file) =>
			{
				foreach (var entry in file.OrderBy(item => item.Order).ThenBy(item => item.Line, StringComparer.Ordinal))
				{
					builder.Line(memberDepth, entry.Line);
					foreach ((int depth, string localLine) in entry.LocalFunctions)
						builder.Line(memberDepth + depth, localLine);
				}
			});
		}

		return builder.ToString();
	}

	/// <summary>
	/// Appends the local functions declared in <paramref name="container"/>, each followed by its own, in
	/// declaration order, one level deeper per nesting.
	/// </summary>
	private static async Task AddLocalFunctionsAsync(
		List<(int Depth, string Line)> lines,
		ISymbol container,
		int depth,
		Solution solution,
		string? solutionDirectory,
		CancellationToken cancellationToken)
	{
		if (container is not (IMethodSymbol or IPropertySymbol or IEventSymbol) || container.DeclaringSyntaxReferences.IsEmpty)
			return;

		IEnumerable<IMethodSymbol> declared = (await LocalFunctions.FindAllInAsync(solution, container, cancellationToken))
			.OrderBy(local => local.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0);
		foreach (IMethodSymbol local in declared)
		{
			lines.Add((depth, Render(local, solution, solutionDirectory).Line));
			await AddLocalFunctionsAsync(lines, local, depth + 1, solution, solutionDirectory, cancellationToken);
		}
	}

	private static (string? Project, string File, int Order, string Line) Render(ISymbol member, Solution solution, string? solutionDirectory)
	{
		SyntaxReference? reference = member.DeclaringSyntaxReferences.FirstOrDefault();
		FileLinePositionSpan? span = reference?.SyntaxTree.GetDisplaySpan(reference.Span);

		string file = span is { } located
			? SolutionRelativePath.Of(solutionDirectory, located.Path)!
			: MetadataBucket;
		string? project = reference is null ? null : ProjectName.Of(solution, reference.SyntaxTree);
		int order = span is { } start ? start.StartLinePosition.Line + 1 : 0;

		string location = span is { } range
			? $"{range.StartLinePosition.Line + 1}:{range.StartLinePosition.Character + 1}-{range.EndLinePosition.Line + 1}:{range.EndLinePosition.Character + 1}"
			: "";

		string line = $"{SymbolKindText.Of(member)},{OutlineBuilder.Field(member.Name)},{location}";

		if (member is IMethodSymbol { Parameters.Length: > 0 } method)
			line += "," + Signature(method);

		return (project, file, order, line);
	}

	private static string Signature(IMethodSymbol method) =>
		string.Join('|', method.Parameters.Select(parameter => OutlineBuilder.Field(parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));

	private static IEnumerable<ISymbol> Collect(INamedTypeSymbol type, bool includeInherited)
	{
		for (INamedTypeSymbol? current = type; current is not null; current = includeInherited ? current.BaseType : null)
		{
			foreach (ISymbol member in current.GetMembers())
				yield return member;
		}
	}
}
