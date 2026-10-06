using SimCube.Roslynk.Core.Features.Conditionals.FindDeadConditionals;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;

namespace SimCube.Roslynk.CoreTests.Features.Conditionals;

public class FindDeadConditionalsTests
{
	[Test]
	public async Task WhenAnIfSymbolIsDefinedInNoConfiguration_ThenItsBranchIsFlagged()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new FindDeadConditionalsTool(registry, new ConditionalCoverage());

		string result = await subject.FindDeadConditionals(TestSolutions.Conditional);

		await Assert.That(result).Contains("NEVERDEFINED");
		await Assert.That(result).Contains("deadConditionals=1");
	}

	[Test]
	public async Task WhenBranchesAreReachableUnderDebugOrRelease_ThenTheyAreNotFlagged()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Conditional);
		var subject = new FindDeadConditionalsTool(registry, new ConditionalCoverage());

		string result = await subject.FindDeadConditionals(TestSolutions.Conditional);

		// #if DEBUG is reachable in the Debug config and its #else in the Release config, so neither is flagged.
		await Assert.That(result).DoesNotContain(",if,DEBUG");
	}
}