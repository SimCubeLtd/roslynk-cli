using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Outlines;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Outlines;

public class OutlineBuilderTests
{
	[Test]
	public async Task WhenHeadersAndBodyAreWritten_ThenABlankLineSeparatesThem()
	{
		var subject = new OutlineBuilder();

		subject.Header("count", 2);
		subject.Status(SolutionStatus.Ready);
		subject.BeginBody();
		subject.Line(0, "first");
		subject.Line(1, "child");

		await Assert.That(subject.ToString()).IsEqualTo("count=2\n\nfirst\n\tchild\n");
	}

	[Test]
	public async Task WhenStatusIsReady_ThenNoStatusHeaderIsWritten()
	{
		var subject = new OutlineBuilder();

		subject.Status(SolutionStatus.Ready);

		await Assert.That(subject.ToString()).IsEqualTo("");
	}

	[Test]
	public async Task WhenStatusIsNotReady_ThenTheStatusHeaderIsWritten()
	{
		var subject = new OutlineBuilder();

		subject.Status(SolutionStatus.Building);

		await Assert.That(subject.ToString()).IsEqualTo("status=Building\n");
	}

	[Test]
	public async Task WhenBeginBodyIsCalledTwice_ThenOnlyOneBlankLineIsWritten()
	{
		var subject = new OutlineBuilder();

		subject.Header("a", "b");
		subject.BeginBody();
		subject.BeginBody();
		subject.Line(0, "x");

		await Assert.That(subject.ToString()).IsEqualTo("a=b\n\nx\n");
	}

	[Test]
	public async Task WhenABooleanHeaderIsWritten_ThenItIsYOrN()
	{
		var subject = new OutlineBuilder();

		subject.Header("truncated", true);
		subject.Header("applied", false);

		await Assert.That(subject.ToString()).IsEqualTo("truncated=Y\napplied=N\n");
	}

	[Test]
	[Arguments("a\r\nb", "a  b")]
	[Arguments("a\nb", "a b")]
	[Arguments("a\rb", "a b")]
	public async Task WhenAValueContainsLineBreaks_ThenSanitizeReplacesThemWithSpaces(string input, string expected)
	{
		await Assert.That(OutlineBuilder.Sanitize(input)).IsEqualTo(expected);
	}

	[Test]
	public async Task WhenAHeaderValueContainsANewline_ThenItIsCollapsedSoTheRecordStaysOnOneLine()
	{
		var subject = new OutlineBuilder();

		subject.Header("errorMessage", "line one\nline two");

		await Assert.That(subject.ToString()).IsEqualTo("errorMessage=line one line two\n");
	}

	[Test]
	public async Task WhenSanitizingNull_ThenAnEmptyStringIsReturned()
	{
		await Assert.That(OutlineBuilder.Sanitize(null)).IsEqualTo("");
	}

	[Test]
	public async Task WhenAFieldContainsAComma_ThenItIsSingleQuoted()
	{
		await Assert.That(OutlineBuilder.Field("Dictionary<string, int>")).IsEqualTo("'Dictionary<string, int>'");
	}

	[Test]
	public async Task WhenAFieldHasNoComma_ThenItIsReturnedUnchanged()
	{
		await Assert.That(OutlineBuilder.Field("Greeter")).IsEqualTo("Greeter");
	}
}