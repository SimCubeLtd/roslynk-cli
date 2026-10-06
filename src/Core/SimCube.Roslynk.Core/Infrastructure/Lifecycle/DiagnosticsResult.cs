using Microsoft.CodeAnalysis;

namespace SimCube.Roslynk.Core.Infrastructure.Lifecycle;

/// <summary>
/// The result of a deferred diagnostics build: the computed <paramref name="Diagnostics"/> together with the
/// exact <paramref name="Solution"/> snapshot they were compiled from, so the caller formats locations
/// against the same trees they were produced from (a write could otherwise swap the snapshot underneath).
/// </summary>
internal sealed record DiagnosticsResult(IReadOnlyList<Diagnostic> Diagnostics, Solution Solution);