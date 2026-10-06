using System.Text.RegularExpressions;
using SimCube.Roslynk.Core.Features.DeadCode.FindDeadCode;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Features.DeadCode.FindDeadCodeTests;

public class FindDeadCodeTests
{
	[Test]
	public async Task WhenAPrivateMethodIsNeverCalled_ThenItIsReportedWithHighConfidence()
	{
		string result = await RunAsync();

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(DeadLeaves(result)).Contains("SimpleLibrary.Widget.Unused");
		// The leaf carries the declaration range then the confidence: 'method,Unused,<loc>,High ...'.
		await Assert.That(result).Matches(new Regex(@"method,Unused,\d+:\d+-\d+:\d+,High"));
	}

	[Test]
	public async Task WhenAMethodIsReferencedOrImplementsAnInterface_ThenItIsNotReported()
	{
		string result = await RunAsync(includePublic: true);

		await Assert.That(DeadLeaves(result)).DoesNotContain("SimpleLibrary.Widget.Compute");
		await Assert.That(DeadLeaves(result)).DoesNotContain("SimpleLibrary.Greeter.Greet");
	}

	[Test]
	public async Task WhenIncludePublicIsFalse_ThenUnreferencedPublicMembersAreNotReported()
	{
		string result = await RunAsync(includePublic: false);

		await Assert.That(DeadLeaves(result)).DoesNotContain("SimpleLibrary.Caller.Run");
	}

	[Test]
	public async Task WhenIncludePublicIsTrue_ThenUnreferencedPublicMembersAreReported()
	{
		string result = await RunAsync(includePublic: true);

		await Assert.That(DeadLeaves(result)).Contains("SimpleLibrary.Caller.Run");
	}

	[Test]
	public async Task WhenTheBodyNestsByFileNamespaceAndType_ThenTheMemberSitsUnderItsContainingType()
	{
		string result = await RunAsync();

		await Assert.That(result.Split('\n')).Contains(line => line == "SimpleLibrary");
		await Assert.That(result).Contains("\tSimpleLibrary\n");
		await Assert.That(result).Contains("\t\tclass,Widget\n");
		await Assert.That(result).Contains("\t\t\tmethod,Unused,");
	}

	[Test]
	public async Task WhenAScopeIsGiven_ThenOnlyMatchingSymbolsAreConsidered()
	{
		string result = await RunAsync(scope: "SimpleLibrary.Widget");

		IReadOnlyList<string> leaves = DeadLeaves(result);
		await Assert.That(leaves).IsNotEmpty();
		foreach (string leaf in leaves)
			await Assert.That(leaf).StartsWith("SimpleLibrary.Widget");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new FindDeadCodeTool(registry);

		string result = await subject.FindDeadCode(TestSolutions.Simple);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static async Task<string> RunAsync(string? scope = null, bool includePublic = false)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindDeadCodeTool(registry);

		return await subject.FindDeadCode(TestSolutions.Simple, scope, includePublic);
	}

	/// <summary>
	/// Reconstructs the fully-qualified name of each reported (leaf) candidate from the nested
	/// file -> namespace -> type -> member outline. A leaf line is 'kind,name,&lt;loc&gt;,&lt;confidence&gt; reason'
	/// (four comma fields, the fourth starting with the confidence); a namespace line has no comma and a
	/// parent type line is just 'kind,name'.
	/// </summary>
	private static IReadOnlyList<string> DeadLeaves(string text)
	{
		var byDepth = new Dictionary<int, (string Name, bool HasComma)>();
		var leaves = new List<string>();

		foreach (string line in text.Split('\n'))
		{
			if (line.Length == 0 || line[0] == '#')
				continue;

			int depth = 0;
			while (depth < line.Length && line[depth] == '\t')
				depth++;

			string content = line[depth..];
			string[] parts = content.Split(',');
			bool hasComma = parts.Length > 1;
			byDepth[depth] = (hasComma ? parts[1] : content, hasComma);
			foreach (int deeper in byDepth.Keys.Where(key => key > depth).ToList())
				byDepth.Remove(deeper);

			bool isLeaf = parts.Length >= 4
				&& (parts[3].StartsWith("High", StringComparison.Ordinal) || parts[3].StartsWith("Medium", StringComparison.Ordinal));
			if (!isLeaf)
				continue;

			// Walk up from the member leaf, collecting the type chain (comma nodes) and the namespace (the
			// first no-comma node); stop there, so the file and project nodes above are excluded.
			var segments = new List<string>();
			for (int level = depth; level >= 0 && byDepth.ContainsKey(level); level--)
			{
				(string name, bool comma) = byDepth[level];
				segments.Insert(0, name);
				if (!comma)
					break;
			}

			leaves.Add(string.Join('.', segments));
		}

		return leaves;
	}
}