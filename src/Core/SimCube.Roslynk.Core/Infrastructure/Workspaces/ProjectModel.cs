using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SimCube.Roslynk.Core.Infrastructure.Workspaces;

/// <summary>Immutable MSBuild-evaluated properties captured for one project at solution-load time.</summary>
/// <param name="Id">The Roslyn project id, stable across in-memory edits.</param>
/// <param name="FilePath">The .csproj path.</param>
/// <param name="CapturedProperties">Property names to evaluated values, e.g. <c>EmitCompilerGeneratedFiles</c>.</param>
internal sealed record ProjectModel(
	ProjectId Id,
	string FilePath,
	ImmutableDictionary<string, string> CapturedProperties);
