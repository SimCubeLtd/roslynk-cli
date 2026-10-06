using SimCube.Roslynk.Core.Infrastructure.Patching;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Patching;

public class PatchApplierTests
{
	[Test]
	public async Task WhenAHunkReplacesALine_ThenTheLineIsChanged()
	{
		string original = "line1\nline2\nline3\n";
		FilePatch patch = ParseSingle(
			"@@ -1,3 +1,3 @@\n" +
			" line1\n" +
			"-line2\n" +
			"+changed\n" +
			" line3\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("line1\nchanged\nline3\n");
	}

	[Test]
	public async Task WhenAHunkAddsLines_ThenTheyAreInserted()
	{
		string original = "a\nb\n";
		FilePatch patch = ParseSingle(
			"@@ -1,2 +1,3 @@\n" +
			" a\n" +
			"+inserted\n" +
			" b\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("a\ninserted\nb\n");
	}

	[Test]
	public async Task WhenTheHunkLineNumbersAreStale_ThenItRelocatesByContent()
	{
		string original = "x\nx\nx\ntarget\ntail\n";
		FilePatch patch = ParseSingle(
			"@@ -42,1 +42,1 @@\n" +
			"-target\n" +
			"+TARGET\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("x\nx\nx\nTARGET\ntail\n");
	}

	[Test]
	public async Task WhenTheContextNoLongerMatches_ThenApplyFails()
	{
		string original = "a\nb\nc\n";
		FilePatch patch = ParseSingle(
			"@@ -1,1 +1,1 @@\n" +
			"-not present anywhere\n" +
			"+replacement\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsFalse();
		await Assert.That(result.FailureReason).IsNotNull();
	}

	[Test]
	public async Task WhenTheFileUsesCrlf_ThenThatLineEndingIsPreserved()
	{
		string original = "a\r\nb\r\nc\r\n";
		FilePatch patch = ParseSingle(
			"@@ -1,3 +1,3 @@\n" +
			" a\n" +
			"-b\n" +
			"+B\n" +
			" c\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("a\r\nB\r\nc\r\n");
	}

	[Test]
	public async Task WhenTheFileHasNoTrailingNewline_ThenNoneIsAdded()
	{
		string original = "a\nb";
		FilePatch patch = ParseSingle(
			"@@ -1,2 +1,2 @@\n" +
			" a\n" +
			"-b\n" +
			"+B\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("a\nB");
	}

	[Test]
	public async Task WhenAContentAnchoredHunkMatchesUniquely_ThenItApplies()
	{
		string original = "a\nb\nc\n";
		FilePatch patch = ParseSingle(
			"@@\n" +
			" a\n" +
			"-b\n" +
			"+B\n" +
			" c\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("a\nB\nc\n");
	}

	[Test]
	public async Task WhenAContentAnchoredHunkMatchesMoreThanOnce_ThenItIsAmbiguousAndFails()
	{
		string original = "dup\ndup\n";
		FilePatch patch = ParseSingle(
			"@@\n" +
			"-dup\n" +
			"+changed\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsFalse();
		await Assert.That(result.FailureReason!).Contains("matched 2");
	}

	[Test]
	public async Task WhenALineNumberedHunkTargetsOneOfTwoIdenticalBlocks_ThenItUsesItsLineNumber()
	{
		string original = "dup\nmid\ndup\n";
		FilePatch patch = ParseSingle(
			"@@ -3,1 +3,1 @@\n" +
			"-dup\n" +
			"+changed\n");

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("dup\nmid\nchanged\n");
	}

	[Test]
	public async Task WhenAFileHasMultipleHunks_ThenAnEarlyInsertionDoesNotOffsetLaterHunks()
	{
		string original = "top\nmid\nbottom\n";
		FilePatch patch = UnifiedDiffParser.Parse(
			"--- a/x.cs\n" +
			"+++ b/x.cs\n" +
			"@@ -1,1 +1,3 @@\n" +
			" top\n" +
			"+inserted1\n" +
			"+inserted2\n" +
			"@@ -3,1 +3,1 @@\n" +
			"-bottom\n" +
			"+BOTTOM\n").Single();

		PatchApplyResult result = PatchApplier.Apply(original, patch);

		await Assert.That(result.Success).IsTrue();
		await Assert.That(result.NewText).IsEqualTo("top\ninserted1\ninserted2\nmid\nBOTTOM\n");
	}

	private static FilePatch ParseSingle(string hunk) =>
		UnifiedDiffParser.Parse("--- a/x.cs\n+++ b/x.cs\n" + hunk).Single();
}