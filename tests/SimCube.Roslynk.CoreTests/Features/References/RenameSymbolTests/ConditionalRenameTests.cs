using System.IO;
using System.Text.RegularExpressions;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.References.RenameSymbolTests;

public class ConditionalRenameTests
{
	[Test]
	public async Task WhenRenamingASymbolUsedInBothBranches_ThenBothIfAndElseOccurrencesAreRewritten()
	{
		// A scratch copy, because rename writes to disk; Caller.Run calls Target.Ping in both #if DEBUG and #else.
		string scratch = TestSolutions.CreateScratchConditionalSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(scratch);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(scratch, "ConditionalLib.Target.Ping", "Pong");

		await Assert.That(result).Contains("applied=Y");

		string caller = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(scratch)!, "ConditionalLib", "Caller.cs"));
		// Single-projection rename would leave the #else call as 'Ping' (1 occurrence); multi-projection rewrites both.
		await Assert.That(Regex.Matches(caller, @"\bPong\b").Count).IsEqualTo(2);
	}
}