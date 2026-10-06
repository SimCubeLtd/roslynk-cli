using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Workspaces;

public class SolutionRelativePathTests
{
	private static string Abs(params string[] segments) =>
		Path.Combine(OperatingSystem.IsWindows() ? @"C:\" : "/", Path.Combine(segments));

	[Test]
	public async Task WhenThePathIsUnderTheSolutionDirectory_ThenItIsMadeRelativeWithForwardSlashes()
	{
		string? result = SolutionRelativePath.Of(Abs("sln"), Abs("sln", "src", "App.cs"));

		await Assert.That(result).IsEqualTo("src/App.cs");
	}

	[Test]
	public async Task WhenThePathIsOutsideTheSolutionDirectory_ThenItWalksOutWithDotDot()
	{
		string? result = SolutionRelativePath.Of(Abs("sln", "app"), Abs("sln", "shared", "Linked.cs"));

		await Assert.That(result).IsEqualTo("../shared/Linked.cs");
	}

	[Test]
	public async Task WhenTheSolutionDirectoryIsUnknown_ThenTheAbsolutePathIsReturnedWithForwardSlashes()
	{
		string absolutePath = Abs("sln", "src", "App.cs");

		string? result = SolutionRelativePath.Of((string?)null, absolutePath);

		await Assert.That(result).IsEqualTo(absolutePath.Replace('\\', '/'));
	}

	[Test]
	public async Task WhenThePathIsNull_ThenNullIsReturned()
	{
		string? result = SolutionRelativePath.Of(Abs("sln"), null);

		await Assert.That(result).IsNull();
	}

	[Test]
	public async Task WhenToAbsoluteIsGivenARootedPath_ThenItIsReturnedAsIs()
	{
		string rootedPath = Abs("other", "App.cs");

		string result = SolutionRelativePath.ToAbsolute(Abs("sln"), rootedPath);

		await Assert.That(result).IsEqualTo(rootedPath);
	}

	[Test]
	public async Task WhenToAbsoluteIsGivenARelativePath_ThenItIsResolvedAgainstTheSolutionDirectory()
	{
		string result = SolutionRelativePath.ToAbsolute(Abs("sln"), Path.Combine("src", "App.cs"));

		await Assert.That(result).IsEqualTo(Abs("sln", "src", "App.cs"));
	}

	[Test]
	public async Task WhenToAbsoluteIsGivenADotDotPath_ThenItWalksOutOfTheSolutionDirectory()
	{
		string result = SolutionRelativePath.ToAbsolute(Abs("sln", "app"), Path.Combine("..", "shared", "B.cs"));

		await Assert.That(result).IsEqualTo(Abs("sln", "shared", "B.cs"));
	}

	[Test]
	public async Task WhenToAbsoluteIsGivenForwardSlashes_ThenTheyAreConvertedToTheOsDelimiter()
	{
		string result = SolutionRelativePath.ToAbsolute(Abs("sln"), "src/nested/App.cs");

		await Assert.That(result).IsEqualTo(Abs("sln", "src", "nested", "App.cs"));
	}
}