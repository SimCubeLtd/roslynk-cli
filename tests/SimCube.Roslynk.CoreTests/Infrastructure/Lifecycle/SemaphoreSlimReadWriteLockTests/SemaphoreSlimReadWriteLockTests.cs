using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.SemaphoreSlimReadWriteLockTests;

public class SemaphoreSlimReadWriteLockTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	[Test]
	public async Task WhenMultipleReadersAcquire_ThenTheyProceedConcurrently()
	{
		using var subject = new SemaphoreSlimReadWriteLock();

		IDisposable first = await subject.AcquireReadAsync();
		IDisposable second = await subject.AcquireReadAsync().WaitAsync(Timeout);

		first.Dispose();
		second.Dispose();
	}

	[Test]
	public async Task WhenAWriterHoldsTheLock_ThenAReaderWaits()
	{
		using var subject = new SemaphoreSlimReadWriteLock();
		IDisposable writer = await subject.AcquireWriteAsync();

		Task<IDisposable> reader = subject.AcquireReadAsync();
		await Task.Delay(50);
		await Assert.That(reader.IsCompleted).IsFalse();

		writer.Dispose();
		IDisposable acquired = await reader.WaitAsync(Timeout);
		acquired.Dispose();
	}

	[Test]
	public async Task WhenReadersHoldTheLock_ThenAWriterWaits()
	{
		using var subject = new SemaphoreSlimReadWriteLock();
		IDisposable reader = await subject.AcquireReadAsync();

		Task<IDisposable> writer = subject.AcquireWriteAsync();
		await Task.Delay(50);
		await Assert.That(writer.IsCompleted).IsFalse();

		reader.Dispose();
		IDisposable acquired = await writer.WaitAsync(Timeout);
		acquired.Dispose();
	}

	[Test]
	public async Task WhenAWriterHoldsTheLock_ThenAnotherWriterWaits()
	{
		using var subject = new SemaphoreSlimReadWriteLock();
		IDisposable first = await subject.AcquireWriteAsync();

		Task<IDisposable> second = subject.AcquireWriteAsync();
		await Task.Delay(50);
		await Assert.That(second.IsCompleted).IsFalse();

		first.Dispose();
		IDisposable acquired = await second.WaitAsync(Timeout);
		acquired.Dispose();
	}

	[Test]
	public async Task WhenAReadIsCancelledWhileWaitingForAWriter_ThenTheReaderCountIsNotLeaked()
	{
		using var subject = new SemaphoreSlimReadWriteLock();
		IDisposable writer = await subject.AcquireWriteAsync();

		using var cancellation = new CancellationTokenSource();
		Task<IDisposable> reader = subject.AcquireReadAsync(cancellation.Token);
		cancellation.Cancel();
		await Assert.That(async () => { await reader; }).Throws<OperationCanceledException>();

		writer.Dispose();

		// If the cancelled first-reader had leaked the count, the write gate would still be held.
		IDisposable nextWriter = await subject.AcquireWriteAsync().WaitAsync(Timeout);
		nextWriter.Dispose();
	}

	[Test]
	public async Task WhenAReleaserIsDisposedTwice_ThenTheSemaphoreIsNotOverReleased()
	{
		using var subject = new SemaphoreSlimReadWriteLock();

		IDisposable reader = await subject.AcquireReadAsync();
		reader.Dispose();
		reader.Dispose();

		// A double release would have over-counted the gate; acquiring a writer proves the count is intact.
		IDisposable writer = await subject.AcquireWriteAsync().WaitAsync(Timeout);
		writer.Dispose();
	}
}