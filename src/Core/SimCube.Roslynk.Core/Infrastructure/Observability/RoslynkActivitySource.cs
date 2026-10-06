using System.Diagnostics;

namespace SimCube.Roslynk.Core.Infrastructure.Observability;

/// <summary>
/// The single <see cref="ActivitySource"/> all Roslynk spans are emitted from.
/// </summary>
internal static class RoslynkActivitySource
{
	public const string Name = "SimCube.Roslynk.Core";

	public static readonly ActivitySource Instance = new(Name);
}
