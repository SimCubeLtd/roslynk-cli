using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.References.FindReferencesTests;

public class ConditionalFindReferencesTests
{
	[Test]
	public async Task WhenASymbolIsUsedInBothIfAndElseBranches_ThenReferencesFromBothBranchesAreFound()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new FindReferencesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindReferences(TestSolutions.Conditional, "ConditionalLib.Target.Ping");

		await Assert.That(result).Contains("resolvedSymbol=ConditionalLib.Target.Ping");

		// The solution loads as DEBUG, so the #if DEBUG call (line 9) is active and the #else call (line 11)
		// sits in disabled text. Multi-projection adds a !DEBUG projection, so both calls are reported.
		await Assert.That(LocationCountUnder(result, "method,Run")).IsEqualTo(2);
	}

	private static int LocationCountUnder(string text, string declaration)
	{
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.TrimStart('\t');
			if (!trimmed.StartsWith(declaration + ",", StringComparison.Ordinal))
				continue;

			string[] parts = trimmed.Split(',');
			return parts[2].Split('|').Length;
		}

		return -1;
	}
}