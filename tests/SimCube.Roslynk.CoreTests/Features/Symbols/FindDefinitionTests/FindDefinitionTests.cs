using System.IO;
using SimCube.Roslynk.Core.Features.Symbols.FindDefinition;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.FindDefinitionTests;

public class FindDefinitionTests
{
	[Test]
	public async Task WhenGivenAUsagePosition_ThenTheDeclarationIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindDefinitionTool(registry, new SymbolResolver(), new ProjectionService());

		string callerPath = Path.Combine(Path.GetDirectoryName(TestSolutions.Simple)!, "SimpleLibrary", "Caller.cs");
		string text = await File.ReadAllTextAsync(callerPath);
		(int line, int column) = ToLineColumn(text, text.IndexOf("Greeter", StringComparison.Ordinal));

		string result = await subject.FindDefinition(TestSolutions.Simple, callerPath, line, column);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("fullName=SimpleLibrary.Greeter");
		await Assert.That(result).Contains("project=SimpleLibrary\n");
		await Assert.That(result).Contains("path=");
		await Assert.That(result).Contains("Greeter.cs");
		await Assert.That(result).Contains("loc=");
	}

	[Test]
	public async Task WhenGivenASolutionRelativeUsagePath_ThenTheDeclarationIsReturned()
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new FindDefinitionTool(registry, new SymbolResolver(), new ProjectionService());

		string solutionDir = Path.GetDirectoryName(TestSolutions.Simple)!;
		string callerPath = Path.Combine(solutionDir, "SimpleLibrary", "Caller.cs");
		string relativePath = Path.GetRelativePath(solutionDir, callerPath).Replace('\\', '/');
		string text = await File.ReadAllTextAsync(callerPath);
		(int line, int column) = ToLineColumn(text, text.IndexOf("Greeter", StringComparison.Ordinal));

		string result = await subject.FindDefinition(TestSolutions.Simple, relativePath, line, column);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("Greeter");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new FindDefinitionTool(registry, new SymbolResolver(), new ProjectionService());

		string callerPath = Path.Combine(Path.GetDirectoryName(TestSolutions.Simple)!, "SimpleLibrary", "Caller.cs");

		string result = await subject.FindDefinition(TestSolutions.Simple, callerPath, 1, 1);

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	private static (int Line, int Column) ToLineColumn(string text, int index)
	{
		int line = 1;
		int column = 1;
		for (int i = 0; i < index; i++)
		{
			if (text[i] == '\n')
			{
				line++;
				column = 1;
			}
			else
			{
				column++;
			}
		}

		return (line, column);
	}
}