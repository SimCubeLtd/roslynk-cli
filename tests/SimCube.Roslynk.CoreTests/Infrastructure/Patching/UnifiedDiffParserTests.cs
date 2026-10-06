using SimCube.Roslynk.Core.Infrastructure.Patching;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Patching;

public class UnifiedDiffParserTests
{
	[Test]
	public async Task WhenParsingASingleHunk_ThenPathsHeaderAndLinesAreRead()
	{
		const string patch =
			"diff --git a/Greeter.cs b/Greeter.cs\n" +
			"index 1111111..2222222 100644\n" +
			"--- a/Greeter.cs\n" +
			"+++ b/Greeter.cs\n" +
			"@@ -3,2 +3,2 @@ namespace SimpleLibrary;\n" +
			" \tpublic string Greet()\n" +
			"-\t\treturn \"Hello\";\n" +
			"+\t\treturn \"Hi\";\n";

		IReadOnlyList<FilePatch> result = UnifiedDiffParser.Parse(patch);

		FilePatch file = await Assert.That(result).HasSingleItem();
		await Assert.That(file.NewPath).IsEqualTo("Greeter.cs");
		await Assert.That(file.OldPath).IsEqualTo("Greeter.cs");
		Hunk hunk = await Assert.That(file.Hunks).HasSingleItem();
		await Assert.That(hunk.OldStart).IsEqualTo(3);
		await Assert.That(hunk.OldLength).IsEqualTo(2);
		await Assert.That(hunk.Lines.Count).IsEqualTo(3);
		await Assert.That(hunk.Lines[0].Kind).IsEqualTo(HunkLineKind.Context);
		await Assert.That(hunk.Lines[1].Kind).IsEqualTo(HunkLineKind.Removed);
		await Assert.That(hunk.Lines[2].Kind).IsEqualTo(HunkLineKind.Added);
	}

	[Test]
	public async Task WhenAHunkHeaderOmitsLengths_ThenTheyDefaultToOne()
	{
		const string patch =
			"--- a/x.cs\n" +
			"+++ b/x.cs\n" +
			"@@ -5 +5 @@\n" +
			"-old\n" +
			"+new\n";

		FilePatch file = await Assert.That(UnifiedDiffParser.Parse(patch)).HasSingleItem();

		Hunk hunk = await Assert.That(file.Hunks).HasSingleItem();
		await Assert.That(hunk.OldLength).IsEqualTo(1);
		await Assert.That(hunk.NewLength).IsEqualTo(1);
	}

	[Test]
	public async Task WhenTheOldSideIsDevNull_ThenTheFileIsMarkedAsCreation()
	{
		const string patch =
			"--- /dev/null\n" +
			"+++ b/New.cs\n" +
			"@@ -0,0 +1,1 @@\n" +
			"+hello\n";

		FilePatch file = await Assert.That(UnifiedDiffParser.Parse(patch)).HasSingleItem();

		await Assert.That(file.IsCreation).IsTrue();
		await Assert.That(file.NewPath).IsEqualTo("New.cs");
	}

	[Test]
	public async Task WhenThePatchTouchesTwoFiles_ThenBothAreParsed()
	{
		const string patch =
			"--- a/A.cs\n" +
			"+++ b/A.cs\n" +
			"@@ -1 +1 @@\n" +
			"-a\n" +
			"+A\n" +
			"--- a/B.cs\n" +
			"+++ b/B.cs\n" +
			"@@ -1 +1 @@\n" +
			"-b\n" +
			"+B\n";

		IReadOnlyList<FilePatch> result = UnifiedDiffParser.Parse(patch);

		await Assert.That(result.Count).IsEqualTo(2);
		await Assert.That(result[0].NewPath).IsEqualTo("A.cs");
		await Assert.That(result[1].NewPath).IsEqualTo("B.cs");
	}

	[Test]
	public async Task WhenTheHunkHeaderHasNoLineNumbers_ThenTheHunkIsStillParsed()
	{
		const string patch =
			"--- a/x.cs\n" +
			"+++ b/x.cs\n" +
			"@@\n" +
			" context\n" +
			"-old\n" +
			"+new1\n" +
			"+new2\n";

		FilePatch file = await Assert.That(UnifiedDiffParser.Parse(patch)).HasSingleItem();

		Hunk hunk = await Assert.That(file.Hunks).HasSingleItem();
		await Assert.That(hunk.HasExplicitPosition).IsFalse();
		await Assert.That(hunk.Lines.Count).IsEqualTo(4);
		await Assert.That(hunk.Lines[0].Kind).IsEqualTo(HunkLineKind.Context);
		await Assert.That(hunk.Lines[1].Kind).IsEqualTo(HunkLineKind.Removed);
		await Assert.That(hunk.Lines[2].Kind).IsEqualTo(HunkLineKind.Added);
		await Assert.That(hunk.Lines[3].Kind).IsEqualTo(HunkLineKind.Added);

		// Lengths are derived from the body: old = context + removed, new = context + added.
		await Assert.That(hunk.OldLength).IsEqualTo(2);
		await Assert.That(hunk.NewLength).IsEqualTo(3);
	}

	[Test]
	public async Task WhenABareHunkHeaderIsFollowedByAnotherFile_ThenTheBodyStopsAtTheNextHeader()
	{
		const string patch =
			"--- a/A.cs\n" +
			"+++ b/A.cs\n" +
			"@@\n" +
			"-a\n" +
			"+A\n" +
			"--- a/B.cs\n" +
			"+++ b/B.cs\n" +
			"@@\n" +
			"-b\n" +
			"+B\n";

		IReadOnlyList<FilePatch> result = UnifiedDiffParser.Parse(patch);

		await Assert.That(result.Count).IsEqualTo(2);
		Hunk first = await Assert.That(result[0].Hunks).HasSingleItem();
		await Assert.That(first.Lines.Count).IsEqualTo(2); // -a / +A only; the next file's "--- " header is not consumed.
		await Assert.That(result[1].NewPath).IsEqualTo("B.cs");
		await Assert.That(result[1].Hunks).HasSingleItem();
	}
}