using TUnit.Assertions.Enums;
using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.RoslynInstanceTests;

public class RoslynInstanceTests
{
	[Test]
	public async Task WhileTheInitialLoadIsInFlight_ThenTheModelIsBuildingWithNoSnapshot()
	{
		var gate = new TaskCompletionSource();
		using var subject = new RoslynInstance(SolutionKey.For(TestSolutions.Simple));

		subject.BeginInitialLoad(
			loader: async progress =>
			{
				await gate.Task;
				return await SolutionWorkspace.LoadAsync(TestSolutions.Simple, progress);
			},
			onReady: _ => { });

		await Assert.That(subject.CurrentModel.Status).IsEqualTo(SolutionStatus.Building);
		await Assert.That(subject.CurrentModel.Solution).IsNull();

		gate.SetResult();
		await subject.WaitUntilReadyAsync();

		await Assert.That(subject.CurrentModel.Status).IsEqualTo(SolutionStatus.Ready);
		await Assert.That(subject.CurrentModel.Solution).IsNotNull();
	}

	[Test]
	public async Task WhenAdvanced_ThenTheNewSolutionIsPublishedAndStatusStaysReady()
	{
		using RoslynInstance subject = await LoadReadyAsync();
		Solution advanced = subject.CurrentSolution;

		subject.AdvanceTo(advanced);

		await Assert.That(subject.CurrentModel.Solution).IsSameReferenceAs(advanced);
		await Assert.That(subject.CurrentModel.Status).IsEqualTo(SolutionStatus.Ready);
	}

	[Test]
	public async Task WhileRebuilding_ThenThePreviousSnapshotIsStillServedAsBuilding()
	{
		using RoslynInstance subject = await LoadReadyAsync();
		Solution previous = subject.CurrentSolution;
		var gate = new TaskCompletionSource();

		subject.BeginRebuild(
			loader: async progress =>
			{
				await gate.Task;
				return await SolutionWorkspace.LoadAsync(TestSolutions.Simple, progress);
			},
			onReady: _ => { });

		await Assert.That(subject.CurrentModel.Status).IsEqualTo(SolutionStatus.Building);
		await Assert.That(subject.CurrentModel.Solution).IsSameReferenceAs(previous);

		gate.SetResult();
		await WaitForReadyAsync(subject);

		await Assert.That(subject.CurrentModel.Status).IsEqualTo(SolutionStatus.Ready);
		await Assert.That(subject.CurrentModel.Solution).IsNotSameReferenceAs(previous);
	}

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	[Test]
	public async Task WhenWritesAreEnqueued_ThenTheyApplyInOrder()
	{
		using RoslynInstance subject = await LoadReadyAsync();

		var order = new List<int>();
		var writes = new List<Task>();
		for (int index = 0; index < 5; index++)
		{
			int captured = index;
			writes.Add(subject.EnqueueWriteAsync((current, token) =>
			{
				lock (order)
					order.Add(captured);
				return Task.FromResult(new WriteResult(current, []));
			}));
		}

		await Task.WhenAll(writes).WaitAsync(Timeout);
		await Assert.That(order).IsEquivalentTo(new[] { 0, 1, 2, 3, 4 }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task WhenAReadStartsDuringAWrite_ThenItWaitsForTheWriteToPublish()
	{
		using RoslynInstance subject = await LoadReadyAsync();

		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		Task write = subject.EnqueueWriteAsync(async (current, token) =>
		{
			started.SetResult();
			await release.Task;
			return new WriteResult(current, []);
		});

		await started.Task.WaitAsync(Timeout);
		Task<SolutionModel> read = subject.ReadModelAsync();
		await Task.Delay(50);
		await Assert.That(read.IsCompleted).IsFalse();

		release.SetResult();
		await write.WaitAsync(Timeout);
		SolutionModel model = await read.WaitAsync(Timeout);
		await Assert.That(model.Status).IsEqualTo(SolutionStatus.Ready);
	}

	[Test]
	public async Task WhenADiagnosticsBuildIsQueuedAfterWrites_ThenTheWritesDrainFirst()
	{
		using RoslynInstance subject = await LoadReadyAsync();

		var events = new List<string>();
		Task write = subject.EnqueueWriteAsync((current, token) =>
		{
			lock (events)
				events.Add("write");
			return Task.FromResult(new WriteResult(current, []));
		});
		Task build = subject.RequestDiagnosticsAsync("key", (solution, token) =>
		{
			lock (events)
				events.Add("build");
			return Task.FromResult<IReadOnlyList<Diagnostic>>([]);
		});

		await Task.WhenAll(write, build).WaitAsync(Timeout);
		await Assert.That(events).IsEquivalentTo(new[] { "write", "build" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task WhenNothingChangedSinceTheLastBuild_ThenTheCachedDiagnosticsAreReused()
	{
		using RoslynInstance subject = await LoadReadyAsync();

		int compiles = 0;
		Task<IReadOnlyList<Diagnostic>> Compute(Solution solution, CancellationToken token)
		{
			Interlocked.Increment(ref compiles);
			return Task.FromResult<IReadOnlyList<Diagnostic>>([]);
		}

		await subject.RequestDiagnosticsAsync("key", Compute).WaitAsync(Timeout);
		await subject.RequestDiagnosticsAsync("key", Compute).WaitAsync(Timeout);
		await Assert.That(compiles).IsEqualTo(1);

		await subject.EnqueueWriteAsync((current, token) => Task.FromResult(new WriteResult(current, []))).WaitAsync(Timeout);
		await subject.RequestDiagnosticsAsync("key", Compute).WaitAsync(Timeout);
		await Assert.That(compiles).IsEqualTo(2);
	}

	[Test]
	public async Task WhenAWriteTransformThrows_ThenLaterWorkStillRuns()
	{
		using RoslynInstance subject = await LoadReadyAsync();

		Task faulting = subject.EnqueueWriteAsync((current, token) => throw new InvalidOperationException("boom"));
		await Assert.That(async () => { await faulting; }).ThrowsExactly<InvalidOperationException>();

		IReadOnlyList<string> changed = await subject
			.EnqueueWriteAsync((current, token) => Task.FromResult(new WriteResult(current, ["after"])))
			.WaitAsync(Timeout);
		await Assert.That(changed).IsEquivalentTo(new[] { "after" }, CollectionOrdering.Matching);
	}

	[Test]
	public async Task WhenDisposed_ThenFurtherWritesFault()
	{
		RoslynInstance subject = await LoadReadyAsync();
		subject.Dispose();

		await Assert.That(async () =>
		{
			await subject.EnqueueWriteAsync((current, token) => Task.FromResult(new WriteResult(current, [])));
		}).Throws<Exception>();
	}
	private static async Task<RoslynInstance> LoadReadyAsync()
	{
		var instance = new RoslynInstance(SolutionKey.For(TestSolutions.Simple));
		instance.BeginInitialLoad(progress => SolutionWorkspace.LoadAsync(TestSolutions.Simple, progress), _ => { });
		await instance.WaitUntilReadyAsync();
		return instance;
	}

	private static async Task WaitForReadyAsync(RoslynInstance instance)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(60);
		while (instance.CurrentModel.Status != SolutionStatus.Ready)
		{
			if (DateTime.UtcNow > deadline)
				throw new TimeoutException("The rebuild did not complete in time.");
			await Task.Delay(25);
		}
	}
}
