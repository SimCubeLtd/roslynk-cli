using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.MultiQuery;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.MultiQuery;

/// <summary>
/// Issue #22 acceptance: the documented impact-analysis recipe (get_symbol + find_references +
/// get_callers + find_implementations + get_type_hierarchy in one multi_query call) runs cleanly
/// against a sample solution - every slot carries that tool's own output and none is error=Invalid.
/// The recipe deliberately uses each tool's own argument name (get_callers is methodName,
/// get_type_hierarchy is typeName): multi_query rejects unknown keys.
/// </summary>
public class MultiQueryImpactRecipeTests
{
	[Test]
	public async Task WhenTheImpactRecipeRunsAgainstASampleSolution_ThenEverySlotIsPopulatedAndNoneIsInvalid()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.AddSingleton<ConditionalCoverage>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		string envelope = await subject.MultiQuery(
			TestSolutions.Simple,
			[
				new(MultiQueryOp.get_symbol, Args(("symbolName", Json("SimpleLibrary.Greeter")))),
				new(MultiQueryOp.find_references, Args(("symbolName", Json("SimpleLibrary.Greeter.Greet")))),
				new(MultiQueryOp.get_callers, Args(("methodName", Json("SimpleLibrary.Greeter.Greet")))),
				new(MultiQueryOp.find_implementations, Args(("symbolName", Json("SimpleLibrary.IGreeter.Greet")))),
				new(MultiQueryOp.get_type_hierarchy, Args(("typeName", Json("SimpleLibrary.Greeter")))),
			]);

		await Assert.That(envelope).Contains("operations=5");
		await Assert.That(envelope).DoesNotContain("error=Invalid");
		await Assert.That(envelope).DoesNotContain("error=NotFound");

		await Assert.That(envelope).Contains("slot=1 tool=get_symbol");
		await Assert.That(envelope).Contains("public class Greeter : IGreeter");      // get_symbol's verbatim declaration

		await Assert.That(envelope).Contains("slot=2 tool=find_references");
		await Assert.That(envelope).Contains("resolvedSymbol=SimpleLibrary.Greeter.Greet");
		await Assert.That(envelope).Contains("method,Run,");                          // Caller.Run is the reference site

		await Assert.That(envelope).Contains("slot=3 tool=get_callers");
		await Assert.That(envelope).Contains("method,Run,");                          // Caller.Run calls Greeter.Greet

		await Assert.That(envelope).Contains("slot=4 tool=find_implementations");
		await Assert.That(envelope).Contains("resolvedSymbol=SimpleLibrary.IGreeter.Greet");
		await Assert.That(envelope).Contains("Greeter");                              // Greeter.Greet implements it

		await Assert.That(envelope).Contains("slot=5 tool=get_type_hierarchy");
		await Assert.That(envelope).Contains("resolvedType=SimpleLibrary.Greeter");
		await Assert.That(envelope).Contains("interfaces");
		await Assert.That(envelope).Contains("IGreeter");
	}

	private static IReadOnlyDictionary<string, JsonElement> Args(params (string Key, JsonElement Value)[] pairs) =>
		MultiQueryTestHelpers.Args(pairs);

	private static JsonElement Json(string value) => MultiQueryTestHelpers.Json(value);
}