using SimCube.Roslynk.Core.Infrastructure.Observability;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Observability;

public class ActivityTagsTests
{
	[Test]
	public async Task WhenTheValueIsNull_ThenNullIsReturned()
	{
		string? result = ActivityTags.Truncate(null);

		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task WhenTheValueIsShorterThanTheLimit_ThenItIsReturnedUnchanged()
	{
		string? result = ActivityTags.Truncate("short");

		await Assert.That(result).IsEqualTo("short");
	}

	[Test]
	public async Task WhenTheValueEqualsTheLimit_ThenItIsReturnedUnchanged()
	{
		string value = new('a', ActivityTags.MaxValueLength);

		string? result = ActivityTags.Truncate(value);

		await Assert.That(result).IsEqualTo(value);
	}

	[Test]
	public async Task WhenTheValueExceedsTheLimit_ThenItIsCappedAtTheLimit()
	{
		string value = new('a', ActivityTags.MaxValueLength + 10);

		string? result = ActivityTags.Truncate(value);

		await Assert.That(result).IsEqualTo(new string('a', ActivityTags.MaxValueLength));
	}
}