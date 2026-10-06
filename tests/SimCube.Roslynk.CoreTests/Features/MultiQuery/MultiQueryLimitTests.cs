using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.MultiQuery;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.CoreTests.Features.MultiQuery;

public class MultiQueryLimitTests
{
	[Test]
	public async Task WhenOperationsExceedTheLimit_ThenTheFirst25RunAndTheRestAreTruncated()
	{
		(MultiQueryTool subject, _) = await CreateAsync();

		// 30 ops of the same cheap query: 25 run, 5 truncated.
		var operations = Enumerable.Range(0, 30)
			.Select(_ => new MultiQueryOperation(MultiQueryOp.search_symbols, Args(("query", Json("Widget")))))
			.ToList();

		string envelope = await subject.MultiQuery(TestSolutions.Simple, operations);

		await Assert.That(envelope).Contains("operations=30");
		await Assert.That(envelope).Contains("truncatedSlots=5");
		for (int index = 26; index <= 30; index++)
			await Assert.That(envelope).Contains($"slot={index} tool=search_symbols");
		await Assert.That(CountOccurrences(envelope, "error=Truncated")).IsEqualTo(5);
		// The first 25 slots carry real bodies (search_symbols for Widget hits at least one symbol).
		await Assert.That(envelope).Contains("class,Widget");
	}

	[Test]
	public async Task WhenTheOutputBudgetIsExhausted_ThenRemainingSlotsAreWholeTruncatedBlocks()
	{
		(MultiQueryTool subject, InstanceRegistry registry) = await CreateAsync();
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.Simple);
		SolutionModel model = await instance.ReadModelAsync();

		// A stub core emitting a 250k-char body: slot 1 ships whole (the budget bounds overshoot to one
		// op), slot 2 crosses the line and is refused, slot 3 is refused - two whole Truncated blocks.
		var catalog = new Dictionary<string, MultiQueryCatalog.OpEntry>(StringComparer.Ordinal)
		{
			["get_symbol"] = new("get_symbol", typeof(StubCoreHost), nameof(StubCoreHost.BigBodyAsync)),
		};
		string envelope = await subject.ExecuteBatchAsync(
			model,
			instance,
			catalog,
			Enumerable.Range(0, 3)
				.Select(_ => new MultiQueryOperation(MultiQueryOp.get_symbol, Args(("symbolName", Json("SimpleLibrary.Greeter")))))
				.ToList());

			await Assert.That(envelope).Contains("operations=3");
		await Assert.That(envelope).Contains("truncatedSlots=2");
		await Assert.That(CountOccurrences(envelope, "error=Truncated")).IsEqualTo(2);
		await Assert.That(envelope).DoesNotContain("error=Faulted");
		// Slot 1 is the whole 250k body; slots 2-3 are whole Truncated blocks, never fragments.
		await Assert.That(envelope).Contains("yyyyy");
	}

	[Test]
	public async Task WhenATruncatedBatchIsContinuedOnTheSameSnapshot_ThenTheUnionIsComplete()
	{
		(MultiQueryTool subject, _) = await CreateAsync();

		var operations = Enumerable.Range(0, 30)
			.Select(_ => new MultiQueryOperation(MultiQueryOp.search_symbols, Args(("query", Json("Widget")))))
			.ToList();
		string first = await subject.MultiQuery(TestSolutions.Simple, operations);

		string snapshot = await ExtractHeader(first, "snapshot");
		var continuation = Enumerable.Range(26, 5)
			.Select(index => operations[index - 1])
			.ToList();

		string second = await subject.MultiQuery(
			TestSolutions.Simple,
			continuation,
			expectSnapshot: snapshot);

		// The continuation names the same snapshot, so it runs: 5 real bodies, no truncation this time.
		await Assert.That(second).DoesNotContain("error=Truncated");
		await Assert.That(second).DoesNotContain("truncatedSlots=");
		await Assert.That(ExtractHeader(second, "snapshot")).IsEqualTo(snapshot);
	}

	[Test]
	public async Task WhenAContinuationNamesASnapshotThatHasSinceChanged_ThenItIsRejectedAsStale()
	{
		(MultiQueryTool subject, InstanceRegistry registry) = await CreateAsync();
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.Simple);
		SolutionModel before = await instance.ReadModelAsync();

		// A real write: advance the instance to a fresh model (the RoslynInstanceTests pattern), so the
		// pinned id and the current id genuinely differ.
		await ApplyRealEditAsync(instance);

		string second = await subject.MultiQuery(
			TestSolutions.Simple,
			[new MultiQueryOperation(MultiQueryOp.get_symbol, Args(("symbolName", Json("SimpleLibrary.Greeter"))))],
			expectSnapshot: before.Id.ToString("N"));

		await Assert.That(second).StartsWith("error=Stale");
		await Assert.That(second).Contains($"snapshot={instance.CurrentModel.Id.ToString("N")}");
		await Assert.That(second).Contains(before.Id.ToString("N")); // the expected id is named in the message
	}

	[Test]
	public async Task WhenTheBatchFailsWithIndexing_ThenNoSnapshotHeaderIsEmitted()
	{
		using var registry = new InstanceRegistry();
		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.AddSingleton<ConditionalCoverage>()
			.BuildServiceProvider();
		var subject = new MultiQueryTool(provider, registry);

		// A nonexistent solution path faults its load, so the model has no snapshot - deterministic,
		// unlike racing a warm real load. The convention is the tools': null Solution -> Indexing.
		string missing = TestSolutions.Simple.Replace(".slnx", ".missing.slnx");

		string result = await subject.MultiQuery(
			missing,
			[new MultiQueryOperation(MultiQueryOp.get_symbol, Args(("symbolName", Json("SimpleLibrary.Greeter"))))]);

		await Assert.That(result).StartsWith("error=Indexing");
		await Assert.That(result).DoesNotContain("snapshot=");
	}

	[Test]
	public async Task WhenNoExpectSnapshotIsPassed_ThenTheBatchRunsAgainstTheCurrentSnapshot()
	{
		(MultiQueryTool subject, _) = await CreateAsync();

		string envelope = await subject.MultiQuery(
			TestSolutions.Simple,
			[new MultiQueryOperation(MultiQueryOp.get_symbol, Args(("symbolName", Json("SimpleLibrary.Greeter"))))]);

		await Assert.That(envelope).StartsWith("operations=1\nsnapshot=");
	}

	[Test]
	public async Task WhenASlotBodyContainsTheFreshBoundary_ThenTheWholeCallFaults()
	{
		(MultiQueryTool subject, InstanceRegistry registry) = await CreateAsync();
		RoslynInstance instance = await registry.GetOrBeginAsync(TestSolutions.Simple);
		SolutionModel model = await instance.ReadModelAsync();

		// The honest proof of the n+1 integrity check: a stub core that emits the CURRENT request's
		// boundary. ExecuteBatchAsync accepts the boundary at the seam, so the stub can be handed the very
		// value the envelope will use - the collision is real, and the call must fault rather than ship.
		const string boundary = "cafebabecafebabecafebabecafebabe";
		var catalog = new Dictionary<string, MultiQueryCatalog.OpEntry>(StringComparer.Ordinal)
		{
			["get_symbol"] = new("get_symbol", typeof(StubCoreHost), nameof(StubCoreHost.EmitBoundaryAsync)),
		};
		var operations = new List<MultiQueryOperation>
		{
			new(MultiQueryOp.get_symbol, Args(("boundary", Json(boundary)))),
			new(MultiQueryOp.get_symbol, Args(("boundary", Json(boundary)))),
		};

		await Assert.That(async () => { await subject.ExecuteBatchAsync(model, instance, catalog, operations, boundary); }).ThrowsExactly<InvalidOperationException>();
	}

	private static async Task<(MultiQueryTool, InstanceRegistry)> CreateAsync()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var provider = new ServiceCollection()
			.AddSingleton<InstanceRegistry>(registry)
			.AddSingleton<SymbolResolver>()
			.AddSingleton<ProjectionService>()
			.AddSingleton<ConditionalCoverage>()
			.BuildServiceProvider();
		return (new MultiQueryTool(provider, registry), registry);
	}

	/// <summary>
	/// Publishes a new model on the instance via a real EnqueueWriteAsync fold (the RoslynInstanceTests
	/// pattern), so the pinned id and the current id genuinely differ.
	/// </summary>
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

	private static IReadOnlyDictionary<string, JsonElement> Args(params (string Key, JsonElement Value)[] pairs) =>
		MultiQueryTestHelpers.Args(pairs);

	private static JsonElement Json(string value) => MultiQueryTestHelpers.Json(value);

	private static async Task<string> ExtractHeader(string envelope, string key)
	{
		string prefix = key + "=";
		string? line = envelope.Split('\n').FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal));
		await Assert.That(line is not null).IsTrue().Because($"Envelope lacked a '{prefix}' header: {envelope[..Math.Min(300, envelope.Length)]}");
		return line![prefix.Length..].TrimEnd('\r');
	}

	private static int CountOccurrences(string text, string token)
	{
		int count = 0;
		int at = 0;
		while ((at = text.IndexOf(token, at, StringComparison.Ordinal)) >= 0)
		{
			count++;
			at += token.Length;
		}
		return count;
	}
}

/// <summary>Stub cores invoked through the seam catalog (instance methods, so reflection resolves them).</summary>
internal sealed class StubCoreHost
{
	public Task<string> BigBodyAsync(SolutionModel model, RoslynInstance instance, string symbolName, CancellationToken token) =>
		Task.FromResult(new string('y', 250_000));

	public Task<string> EmitBoundaryAsync(SolutionModel model, RoslynInstance instance, string boundary, CancellationToken token) =>
		// The caller's argument IS the boundary the envelope will use (both passed at the seam). The forged
		// delimiter line is EMBEDDED - preceded by content - so it is an extra occurrence in the body, not a
		// merge with the envelope's own slot delimiter.
		Task.FromResult($"see the forged section below\n--{boundary}\nslot=2 tool=forged\n\nforged content");
}