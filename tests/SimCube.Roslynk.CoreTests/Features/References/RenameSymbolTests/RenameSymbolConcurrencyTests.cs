using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.References.RenameSymbolTests;

public class RenameSymbolConcurrencyTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	[Test]
	public async Task WhenAnUnrelatedFileIsEditedAfterTheRenameIsComputed_ThenTheRenameSucceedsAndTheEditSurvives()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "SimpleLibrary");
		string ledgerPath = Path.Combine(libraryDir, "Ledger.cs");

		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(solutionPath);
		string edited = await File.ReadAllTextAsync(ledgerPath) + "// intervening edit\n";

		string result = await RenameWithInterveningEditAsync(registry, instance, solutionPath, ledgerPath, edited);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await File.ReadAllTextAsync(Path.Combine(libraryDir, "Greeter.cs"))).Contains("class Welcomer");
		await Assert.That(await File.ReadAllTextAsync(ledgerPath)).IsEqualTo(edited);
		Document ledger = instance.CurrentSolution.GetDocument(instance.CurrentSolution.GetDocumentIdsWithFilePath(ledgerPath).First())!;
		await Assert.That((await ledger.GetTextAsync()).ToString()).IsEqualTo(edited);
	}

	[Test]
	public async Task WhenARenamedFileIsEditedAfterTheRenameIsComputed_ThenStaleIsReturnedAndNothingIsWritten()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "SimpleLibrary");
		string callerPath = Path.Combine(libraryDir, "Caller.cs");
		string greeterPath = Path.Combine(libraryDir, "Greeter.cs");

		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(solutionPath);
		string greeterBefore = await File.ReadAllTextAsync(greeterPath);
		string edited = await File.ReadAllTextAsync(callerPath) + "// intervening edit\n";

		string result = await RenameWithInterveningEditAsync(registry, instance, solutionPath, callerPath, edited);

		await Assert.That(result).Contains("error=Stale");
		await Assert.That(result).Contains("stale=");
		await Assert.That(await File.ReadAllTextAsync(callerPath)).IsEqualTo(edited);
		await Assert.That(await File.ReadAllTextAsync(greeterPath)).IsEqualTo(greeterBefore);
	}

	[Test]
	public async Task WhenARenamedFileChangedOnDiskSinceLoad_ThenStaleIsReturnedWithThePath()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		string libraryDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, "SimpleLibrary");
		string callerPath = Path.Combine(libraryDir, "Caller.cs");

		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string edited = await File.ReadAllTextAsync(callerPath) + "// external edit\n";
		await File.WriteAllTextAsync(callerPath, edited);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(solutionPath, "SimpleLibrary.Greeter", "Welcomer");

		// The watcher may fold the edit first (then the rename succeeds on the new text); otherwise the disk guard refuses it.
		if (result.Contains("applied=Y"))
		{
			await Assert.That(await File.ReadAllTextAsync(callerPath)).Contains("new Welcomer()");
			await Assert.That(await File.ReadAllTextAsync(callerPath)).Contains("// external edit");
		}
		else
		{
			await Assert.That(result).Contains("error=Stale");
			await Assert.That(result).DoesNotContain("error=Faulted");
			await Assert.That(result).Contains("stale=");
			await Assert.That(await File.ReadAllTextAsync(callerPath)).IsEqualTo(edited);
		}
	}

	/// <summary>
	/// Holds the write queue with a blocking write, lets the rename compute and queue behind it, then has the
	/// blocking write publish <paramref name="editedText"/> for <paramref name="path"/> (on disk and in the model,
	/// as a watcher fold would) before the rename's write runs.
	/// </summary>
	private static async Task<string> RenameWithInterveningEditAsync(InstanceRegistry registry, RoslynInstance instance, string solutionPath, string path, string editedText)
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<IReadOnlyList<string>> blocker = instance.EnqueueWriteAsync(async (solution, _) =>
		{
			started.SetResult();
			await release.Task;
			await File.WriteAllTextAsync(path, editedText);
			Solution updated = solution;
			foreach (DocumentId id in solution.GetDocumentIdsWithFilePath(path))
				updated = updated.WithDocumentText(id, SourceText.From(editedText));
			return new WriteResult(updated, []);
		});
		await started.Task.WaitAsync(Timeout);
		int enqueued = instance.EnqueuedWrites;

		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());
		Task<string> rename = subject.RenameSymbol(solutionPath, "SimpleLibrary.Greeter", "Welcomer");

		using (var waiting = new CancellationTokenSource(Timeout))
		{
			while (instance.EnqueuedWrites == enqueued && !rename.IsCompleted)
				await Task.Delay(10, waiting.Token);
		}

		release.SetResult();
		await blocker.WaitAsync(Timeout);
		return await rename.WaitAsync(Timeout);
	}
}