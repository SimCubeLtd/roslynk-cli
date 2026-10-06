using System.Diagnostics.Metrics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Observability;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Observability.SolutionMetricsTests;

public class SolutionMetricsTests
{
	[Test]
	public async Task WhenASolutionIsOpen_ThenItIsReportedAsOneTaggedWithItsPath()
	{
		using var meter = new Meter("test-" + Guid.NewGuid().ToString("N"));
		using var registry = new InstanceRegistry();
		var subject = new SolutionMetrics(meter, registry);
		List<(int Value, string? Path)> measurements = Collect(meter, out MeterListener listener);
		using (listener)
		{
			await registry.GetOrAddAsync(TestSolutions.Simple);
			listener.RecordObservableInstruments();
		}

		(int Value, string? Path) reported = await Assert.That(measurements).HasSingleItem();
		await Assert.That(reported.Value).IsEqualTo(1);
		await Assert.That(reported.Path).IsEqualTo(registry.OpenSolutionPaths.Single());
		await Assert.That(reported.Path).Contains("SimpleSolution");
	}

	[Test]
	public async Task WhenAllSolutionsAreClosed_ThenNothingIsReported()
	{
		using var meter = new Meter("test-" + Guid.NewGuid().ToString("N"));
		using var registry = new InstanceRegistry();
		var subject = new SolutionMetrics(meter, registry);
		List<(int Value, string? Path)> measurements = Collect(meter, out MeterListener listener);
		using (listener)
		{
			await registry.GetOrAddAsync(TestSolutions.Simple);
			registry.TryClose(TestSolutions.Simple);
			listener.RecordObservableInstruments();
		}

		await Assert.That(measurements).IsEmpty();
	}

	[Test]
	public async Task WhenConstructedWithoutAMeter_ThenItThrows()
	{
		using var registry = new InstanceRegistry();

		await Assert.That(() => new SolutionMetrics(null!, registry)).ThrowsExactly<ArgumentNullException>();
	}

	[Test]
	public async Task WhenConstructedWithoutARegistry_ThenItThrows()
	{
		using var meter = new Meter("test-" + Guid.NewGuid().ToString("N"));

		await Assert.That(() => new SolutionMetrics(meter, null!)).ThrowsExactly<ArgumentNullException>();
	}

	private static List<(int Value, string? Path)> Collect(Meter meter, out MeterListener listener)
	{
		var measurements = new List<(int Value, string? Path)>();
		listener = new MeterListener();
		listener.InstrumentPublished = (instrument, activeListener) =>
		{
			if (instrument.Meter == meter && instrument.Name == SolutionMetrics.OpenSolutionsName)
				activeListener.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
		{
			string? path = null;
			foreach (KeyValuePair<string, object?> tag in tags)
			{
				if (tag.Key == SolutionMetrics.SolutionPathTag)
					path = tag.Value as string;
			}

			measurements.Add((measurement, path));
		});
		listener.Start();
		return measurements;
	}
}