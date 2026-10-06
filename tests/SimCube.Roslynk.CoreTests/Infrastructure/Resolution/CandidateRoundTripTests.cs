using SimCube.Roslynk.Core.Features.Callers.GetCallers;
using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbol;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Resolution;

/// <summary>
/// The contract issue #34 is about: the candidates a tool prints on an ambiguous match are exactly the
/// strings that tool accepts, so a caller resolves the ambiguity by copying one line back. Each case drives
/// a tool with a name that matches several symbols, then re-runs it with every candidate it emitted.
/// </summary>
public class CandidateRoundTripTests
{
	private const string AmbiguousMethod = "SimpleLibrary.Overloads.Pick";

	[Test]
	public async Task WhenGetSymbolBodyReportsAmbiguity_ThenEveryCandidateResolvesOnRetry()
	{
		await AssertRoundTripAsync(AmbiguousMethod, async (registry, name) =>
			await new GetSymbolBodyTool(registry, new SymbolResolver(), new ProjectionService())
				.GetSymbolBody(TestSolutions.Simple, name));
	}

	[Test]
	public async Task WhenGetSymbolReportsAmbiguity_ThenEveryCandidateResolvesOnRetry()
	{
		await AssertRoundTripAsync(AmbiguousMethod, async (registry, name) =>
			await new GetSymbolTool(registry, new SymbolResolver(), new ProjectionService())
				.GetSymbol(TestSolutions.Simple, name));
	}

	[Test]
	public async Task WhenGetCallersReportsAmbiguity_ThenEveryCandidateResolvesOnRetry()
	{
		await AssertRoundTripAsync(AmbiguousMethod, async (registry, name) =>
			await new GetCallersTool(registry, new SymbolResolver(), new ProjectionService())
				.GetCallers(TestSolutions.Simple, name));
	}

	[Test]
	public async Task WhenFindReferencesReportsAmbiguity_ThenEveryCandidateResolvesOnRetry()
	{
		await AssertRoundTripAsync(AmbiguousMethod, async (registry, name) =>
			await new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService())
				.FindReferences(TestSolutions.Simple, name));
	}

	/// <summary>
	/// Drives <paramref name="run"/> with an ambiguous name, asserts the failure carries distinguishable
	/// candidates, then re-runs it with each of them and asserts each one resolved.
	/// </summary>
	private static async Task AssertRoundTripAsync(string ambiguousName, Func<InstanceRegistry, string, Task<string>> run)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);

		string ambiguous = await run(registry, ambiguousName);
		await Assert.That(ambiguous).Contains("error=Ambiguous");

		IReadOnlyList<string> candidates = Candidates(ambiguous);
		await Assert.That(candidates.Count > 1).IsTrue().Because($"Expected several candidates, got: {ambiguous}");
		await Assert.That(candidates.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(candidates.Count);

		foreach (string candidate in candidates)
		{
			string resolved = await run(registry, candidate);

			await Assert.That(resolved.Contains("error=Ambiguous", StringComparison.Ordinal)
				|| resolved.Contains("error=NotFound", StringComparison.Ordinal)).IsFalse().Because($"Candidate '{candidate}' did not resolve: {resolved}");
		}
	}

	private static IReadOnlyList<string> Candidates(string result) =>
		result
			.Split('\n')
			.Where(line => line.StartsWith("candidate=", StringComparison.Ordinal))
			.Select(line => line["candidate=".Length..].TrimEnd('\r'))
			.ToArray();
}