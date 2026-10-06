using System.IO;
using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.InstanceRegistryTests;

public class GetOrBeginTests
{
	[Test]
	public async Task WhenTheSameSolutionIsRequestedTwice_ThenTheSameInstanceIsShared()
	{
		using var subject = new InstanceRegistry();

		RoslynInstance first = subject.GetOrBegin(TestSolutions.Simple);
		RoslynInstance second = subject.GetOrBegin(TestSolutions.Simple);

		await Assert.That(second).IsSameReferenceAs(first);
	}

	[Test]
	public async Task WhenADirtyInstanceIsRequested_ThenItIsRebuiltAndPicksUpTheDiskChange()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		using var subject = new InstanceRegistry();
		RoslynInstance instance = await subject.GetOrAddAsync(solutionPath);

		// Mimic a build-file / additional-document edit: change a source file on disk and mark the snapshot
		// dirty (what SolutionFileSync does for a change it cannot fold incrementally, e.g. a .mixin edit).
		string greeter = Directory
			.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, "Greeter.cs", SearchOption.AllDirectories)
			.First();
		await File.WriteAllTextAsync(greeter, (await File.ReadAllTextAsync(greeter)) + "\n// rebuilt from disk\n");
		instance.MarkDirty();

		// The non-blocking read path serves the stale snapshot while a background rebuild runs.
		RoslynInstance same = subject.GetOrBegin(solutionPath);
		await Assert.That(same).IsSameReferenceAs(instance);
		await WaitForReadyAsync(instance);

		await Assert.That(instance.IsDirty).IsFalse();
		DocumentId id = instance.CurrentSolution.GetDocumentIdsWithFilePath(greeter).First();
		string text = (await instance.CurrentSolution.GetDocument(id)!.GetTextAsync()).ToString();
		await Assert.That(text).Contains("rebuilt from disk");
	}

	[Test]
	public async Task WhenACleanInstanceIsRequested_ThenTheSnapshotIsNotReplaced()
	{
		using var subject = new InstanceRegistry();
		RoslynInstance instance = await subject.GetOrAddAsync(TestSolutions.Simple);
		Solution before = instance.CurrentSolution;

		subject.GetOrBegin(TestSolutions.Simple);

		await Assert.That(instance.CurrentSolution).IsSameReferenceAs(before);
	}

	[Test]
	public async Task WhenADirtyInstanceIsRequestedViaGetOrBeginAsync_ThenOneCallReturnsTheRebuiltSnapshot()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		using var subject = new InstanceRegistry();
		RoslynInstance instance = await subject.GetOrAddAsync(solutionPath);

		// Mimic an additional-document edit (e.g. a .mixin): change a source file on disk and mark dirty.
		string greeter = Directory
			.EnumerateFiles(Path.GetDirectoryName(solutionPath)!, "Greeter.cs", SearchOption.AllDirectories)
			.First();
		await File.WriteAllTextAsync(greeter, (await File.ReadAllTextAsync(greeter)) + "\n// rebuilt in one call\n");
		instance.MarkDirty();

		// The blocking read path awaits the rebuild, so a single call returns a Ready, up-to-date snapshot.
		RoslynInstance same = await subject.GetOrBeginAsync(solutionPath);

		await Assert.That(same).IsSameReferenceAs(instance);
		await Assert.That(instance.CurrentModel.Status).IsEqualTo(SolutionStatus.Ready);
		await Assert.That(instance.IsDirty).IsFalse();
		DocumentId id = instance.CurrentSolution.GetDocumentIdsWithFilePath(greeter).First();
		string text = (await instance.CurrentSolution.GetDocument(id)!.GetTextAsync()).ToString();
		await Assert.That(text).Contains("rebuilt in one call");
	}

	[Test]
	public async Task WhenACleanInstanceIsRequestedViaGetOrBeginAsync_ThenTheSnapshotIsNotReplaced()
	{
		using var subject = new InstanceRegistry();
		RoslynInstance instance = await subject.GetOrAddAsync(TestSolutions.Simple);
		Solution before = instance.CurrentSolution;

		RoslynInstance same = await subject.GetOrBeginAsync(TestSolutions.Simple);

		await Assert.That(same).IsSameReferenceAs(instance);
		await Assert.That(instance.CurrentSolution).IsSameReferenceAs(before);
		await Assert.That(instance.CurrentModel.Status).IsEqualTo(SolutionStatus.Ready);
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