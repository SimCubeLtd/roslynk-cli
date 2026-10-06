using System.IO;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Writing;

public class AtomicFileWriterTests
{
	[Test]
	public async Task WhenWritingABatch_ThenEveryFileGetsItsNewContentAndTempsAreCleanedUp()
	{
		string directory = Path.Combine(Path.GetTempPath(), "roslynk-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string first = Path.Combine(directory, "first.txt");
		string second = Path.Combine(directory, "second.txt");
		await File.WriteAllTextAsync(first, "old-first");
		await File.WriteAllTextAsync(second, "old-second");

		await AtomicFileWriter.WriteAllAsync(
		[
			new PendingWrite(first, "new-first"),
			new PendingWrite(second, "new-second"),
		]);

		await Assert.That(await File.ReadAllTextAsync(first)).IsEqualTo("new-first");
		await Assert.That(await File.ReadAllTextAsync(second)).IsEqualTo("new-second");
		await Assert.That(File.Exists(first + ".roslynk.tmp")).IsFalse();
		await Assert.That(File.Exists(first + ".roslynk.bak")).IsFalse();
	}
}