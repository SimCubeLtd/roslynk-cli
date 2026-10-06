using Microsoft.CodeAnalysis;
using SimCube.Roslynk.Core.Infrastructure.Workspaces;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Workspaces.SolutionWorkspaceTests;

public class LoadAsyncTests
{
	[Test]
	public async Task WhenLoadingASolution_ThenItsProjectsAndDocumentsAreAvailable()
	{
		using SolutionWorkspace subject = await SolutionWorkspace.LoadAsync(TestSolutions.Simple);

		Project project = await Assert.That(subject.Solution.Projects).HasSingleItem();

		await Assert.That(project.Name).IsEqualTo("SimpleLibrary");
		await Assert.That(project.Documents).Contains(document => document.Name == "Greeter.cs");
	}
}