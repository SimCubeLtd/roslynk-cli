using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Lifecycle.InstanceRegistryTests;

public class InstanceRegistryTests
{
	[Test]
	public async Task WhenAnInstanceIsNotIdleLongEnough_ThenItIsNotEvicted()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);

		int evicted = registry.EvictIdle(TimeSpan.FromHours(1), DateTime.UtcNow);

		await Assert.That(evicted).IsEqualTo(0);
		await Assert.That(registry.LoadedInstances()).HasSingleItem();
	}

	[Test]
	public async Task WhenAnInstanceHasBeenIdle_ThenItIsEvicted()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);

		int evicted = registry.EvictIdle(TimeSpan.Zero, DateTime.UtcNow.AddDays(1));

		await Assert.That(evicted).IsEqualTo(1);
		await Assert.That(registry.LoadedInstances()).IsEmpty();
	}
}