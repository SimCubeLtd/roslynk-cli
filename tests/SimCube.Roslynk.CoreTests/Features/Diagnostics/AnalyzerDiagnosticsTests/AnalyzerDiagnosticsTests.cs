using SimCube.Roslynk.Core.Features.Diagnostics.GetDiagnostics;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.Diagnostics.AnalyzerDiagnosticsTests;

public class AnalyzerDiagnosticsTests
{
	[Test]
	public async Task WhenAnAnalyzerReportsAPrivateFixTrigger_ThenOnlyThePublicIdIsListed()
	{
		// The IDE0005 analyzer reports a second, message-less diagnostic that exists only to trigger its
		// fixer; listing it would give an agent an id with nothing to act on.
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.CodeStyle);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(
			TestSolutions.CodeStyle, includeWarnings: true, includeInfo: true, includeHidden: true);

		await Assert.That(result).Contains("IDE0005");
		await Assert.That(result).DoesNotContain("RemoveUnnecessaryImportsFixable");
	}

	[Test]
	public async Task WhenAnalyzersAreIncluded_ThenNonCompilerDiagnosticsAppear()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(
			TestSolutions.Simple, includeWarnings: true, includeInfo: true, includeHidden: true, includeAnalyzers: true);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(DiagnosticIds(result)).Contains(id => !id.StartsWith("CS", StringComparison.Ordinal));
	}

	[Test]
	public async Task WhenAnalyzersAreExcluded_ThenOnlyCompilerDiagnosticsAppear()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(
			TestSolutions.Simple, includeWarnings: true, includeInfo: true, includeHidden: true, includeAnalyzers: false);

		await Assert.That(result).DoesNotContain("error=");
		foreach (string id in DiagnosticIds(result))
			await Assert.That(id).StartsWith("CS");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetDiagnosticsTool(registry, new DiagnosticsService());

		string result = await subject.GetDiagnostics(TestSolutions.Simple);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static IReadOnlyList<string> DiagnosticIds(string text)
	{
		// An entry line is '<id>,<line:col>,<message>' (or '<id>,<message>' when there is no location) and sits
		// exactly one tab deeper than its severity label (errors|warnings|infos|hidden). Structural lines can
		// legitimately contain ',' or ' ' — e.g. the generated file obj/.../.NETCoreApp,Version=v8.0.AssemblyAttributes.cs —
		// so depth relative to the severity label, not line content, decides what is an entry.
		var ids = new List<string>();
		int entryDepth = -1;
		foreach (string raw in text.Split('\n'))
		{
			string content = raw.TrimStart('\t');
			int depth = raw.Length - content.Length;

			if (depth > 0 && content is "errors" or "warnings" or "infos" or "hidden")
			{
				entryDepth = depth + 1;
				continue;
			}

			if (depth == entryDepth)
			{
				ids.Add(content[..content.IndexOf(',')]);
				continue;
			}

			if (depth < entryDepth)
				entryDepth = -1;
		}

		return ids;
	}
}