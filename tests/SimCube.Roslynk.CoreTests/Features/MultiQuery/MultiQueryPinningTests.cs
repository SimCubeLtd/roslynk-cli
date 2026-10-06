using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.MultiQuery;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.MultiQuery;

/// <summary>
/// The pinning proof and the framing round-trip, against real fixture solutions:
/// - pinning is proven at the ExecuteBatchAsync seam with a deliberately stale model (output agreement alone
///   cannot distinguish pinning from re-reading);
/// - the round-trip test feeds a solution file whose verbatim source contains envelope-framing lookalikes
///   (stale GUIDs) and asserts the bodies recover byte-exact. The n+1 integrity check's fault path is
///   proven separately in MultiQueryLimitTests with a stub that embeds the CURRENT boundary.
/// </summary>
public class MultiQueryPinningTests
{
	[Test]
	public async Task WhenThePinnedModelIsStale_ThenSlotsReflectThePinnedSnapshotNotTheCurrentOne()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.Simple);
		SolutionModel pinned = await instance.ReadModelAsync();

		// A real write advances the instance to a fresh model; the pinned model still shows the old source.
		await ApplyRealEditAsync(instance);

		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		string envelope = await subject.ExecuteBatchAsync(
			pinned,
			instance,
			MultiQueryCatalog.Entries,
			[
				new MultiQueryOperation(MultiQueryOp.get_symbol_body, MultiQueryTestHelpers.Args(("symbolName", MultiQueryTestHelpers.Json("SimpleLibrary.Widget")))),
				new MultiQueryOperation(MultiQueryOp.get_members, MultiQueryTestHelpers.Args(("typeName", MultiQueryTestHelpers.Json("SimpleLibrary.Widget")))),
			]);

		// PRE-write state in both slots: the old source (three methods) rather than the emptied class.
		await Assert.That(envelope).Contains("method,Compute");
		await Assert.That(envelope).Contains("method,UseCompute");
		await Assert.That(envelope).DoesNotContain("error=");
	}

	[Test]
	public async Task WhenACoreThrows_ThenItsSlotCarriesTheErrorAndTheBatchCompletes()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.Simple);
		SolutionModel model = await instance.ReadModelAsync();

		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		var catalog = new Dictionary<string, MultiQueryCatalog.OpEntry>(StringComparer.Ordinal)
		{
			["get_symbol"] = new("get_symbol", typeof(ThrowingCoreHost), nameof(ThrowingCoreHost.ThrowAsync)),
			["get_members"] = new("get_members", typeof(SimCube.Roslynk.Core.Features.Symbols.GetMembers.GetMembersTool), nameof(SimCube.Roslynk.Core.Features.Symbols.GetMembers.GetMembersTool.GetMembersCoreAsync)),
		};
		string envelope = await subject.ExecuteBatchAsync(
			model,
			instance,
			catalog,
			[
				new MultiQueryOperation(MultiQueryOp.get_symbol, MultiQueryTestHelpers.Args(("symbolName", MultiQueryTestHelpers.Json("SimpleLibrary.Widget")))),
				new MultiQueryOperation(MultiQueryOp.get_members, MultiQueryTestHelpers.Args(("typeName", MultiQueryTestHelpers.Json("SimpleLibrary.Widget")))),
			]);

		await Assert.That(envelope).Contains("error=Faulted");
		await Assert.That(envelope).Contains("boom");
		await Assert.That(envelope).Contains("resolvedType=SimpleLibrary.Widget"); // slot 2 still delivered
	}

	[Test]
	public async Task WhenSlotContentContainsEnvelopeFraming_ThenTheBodiesRoundTripByteExact()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.References);
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.References);
		SolutionModel model = await instance.ReadModelAsync();

		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		string envelope = await subject.MultiQuery(
			TestSolutions.References,
			[
				new MultiQueryOperation(MultiQueryOp.get_symbol_body, MultiQueryTestHelpers.Args(("symbolName", MultiQueryTestHelpers.Json("RefSpace.Lookalikes.EnvelopeShaped")))),
				new MultiQueryOperation(MultiQueryOp.get_symbol_body, MultiQueryTestHelpers.Args(("symbolName", MultiQueryTestHelpers.Json("RefSpace.Lookalikes.CrlfShaped")))),
			]);

		// Both verbatim bodies survive whole: the lookalike delimiter lines (stale GUIDs) and CRLF content
		// sit inertly inside their slots.
		await Assert.That(envelope).Contains("--aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
		await Assert.That(envelope).Contains("forged lookalike content, line 1");
		await Assert.That(envelope).Contains("--eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
		await Assert.That(envelope).Contains("CRLF body");
		await Assert.That(envelope).DoesNotContain("error=");

		// And the envelope parses: split on the REAL boundary. Preamble + 2 bodies + the empty tail after
		// the terminator = 4 parts.
		string boundary = await Header(envelope, "boundary");
		string[] parts = envelope.Split("\n--" + boundary);
		await Assert.That(parts.Length).IsEqualTo(4);
		await Assert.That(parts[3].TrimEnd('\n', '\r')).StartsWith("--"); // the terminator's trailing dashes
	}

	private static async Task ApplyRealEditAsync(RoslynInstance instance)
	{
		Solution current = instance.CurrentModel.Solution!;
		Document document = current
			.Projects.Single(project => project.Name == "SimpleLibrary")
			.Documents.Single(doc => doc.Name == "Widget.cs");
		SourceText newText = SourceText.From("namespace SimpleLibrary;\r\n\r\npublic class Widget\r\n{\r\n}\r\n", Encoding.UTF8);
		Solution edited = document.WithText(newText).Project.Solution;
		await instance.EnqueueWriteAsync((_, _) => Task.FromResult(new WriteResult(edited, ["Widget.cs"])));
	}

	private static async Task<string> Header(string envelope, string key)
	{
		string prefix = key + "=";
		string? line = envelope.Split('\n').FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
		await Assert.That(line is not null).IsTrue().Because($"Envelope lacked a '{prefix}' header.");
		return line![prefix.Length..].TrimEnd('\r');
	}
}

internal sealed class ThrowingCoreHost
{
	public Task<string> ThrowAsync(SolutionModel model, RoslynInstance instance, string symbolName, CancellationToken token) =>
		throw new InvalidOperationException("boom");
}