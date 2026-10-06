using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Application;

namespace SimCube.Roslynk.CoreTests.Server;

public sealed class ApplicationBoundaryTests
{
	[Test]
	public async Task WhenCorePublicContractsAreInspected_ThenRoslynAndTransportTypesDoNotEscape()
	{
		Assembly core = typeof(RoslynkApplication).Assembly;
		await Assert.That(core.GetReferencedAssemblies()).DoesNotContain(assembly => assembly.Name!.Contains("MessagePack") || assembly.Name.Contains("ModelContextProtocol") || assembly.Name.Contains("AspNetCore") || assembly.Name.Contains("CommandLine"));
		foreach (Type type in core.GetExportedTypes())
		{
			await Assert.That(type.Namespace is "SimCube.Roslynk.Core.Application" or "SimCube.Roslynk.Core").IsTrue();
			foreach (PropertyInfo property in type.GetProperties()) await Check(property.PropertyType);
			foreach (ConstructorInfo constructor in type.GetConstructors())
				foreach (ParameterInfo parameter in constructor.GetParameters()) await Check(parameter.ParameterType);
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static))
			{
				await Check(method.ReturnType);
				foreach (ParameterInfo parameter in method.GetParameters()) await Check(parameter.ParameterType);
			}
		}
	}
	[Test]
	public async Task WhenTypedBatchDefaultsAreNull_ThenTheyMatchTheStandaloneQuery()
	{
		var services = new ServiceCollection();
		services.AddRoslynk();
		using ServiceProvider provider = services.BuildServiceProvider();
		RoslynkApplication application = provider.GetRequiredService<RoslynkApplication>();
		OperationResult standalone = await application.GetMembersAsync(new(TestSolutions.Simple, "SimpleLibrary.Calculator"));
		OperationResult batch = await application.MultiQueryAsync(new(TestSolutions.Simple, [new GetMembersQuery(new(TestSolutions.Simple, "SimpleLibrary.Calculator"))]));
		await Assert.That(standalone.Error).IsNull(); await Assert.That(batch.Error).IsNull();
		await Assert.That(standalone.Text).Contains("Add"); await Assert.That(batch.Text).Contains("Add");
	}

	[Test]
	public async Task WhenAnInitialLoadFaults_ThenExplicitReloadCanRecoverWithoutRestartingTheDaemon()
	{
		string directory = Path.Combine(Path.GetTempPath(), "rk-reload-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string solution = Path.Combine(directory, "Fixture.slnx");
		await File.WriteAllTextAsync(solution, "invalid solution");
		try
		{
			var services = new ServiceCollection(); services.AddRoslynk();
			using ServiceProvider provider = services.BuildServiceProvider();
			RoslynkApplication application = provider.GetRequiredService<RoslynkApplication>();
			OperationResult failed = await application.GetDiagnosticsAsync(new(solution));
			await Assert.That(failed.Error!.Code).IsEqualTo("Faulted");
			string project = Path.Combine(Path.GetDirectoryName(TestSolutions.Simple)!, "SimpleLibrary", "SimpleLibrary.csproj");
			var xml = new System.Xml.Linq.XElement("Solution", new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Path", project)));
			await File.WriteAllTextAsync(solution, xml.ToString());
			OperationResult reloading = await application.ReloadSolutionAsync(new(solution));
			await Assert.That(reloading.Error).IsNull();
			OperationResult ready = await application.GetMembersAsync(new(solution, "SimpleLibrary.Calculator"));
			await Assert.That(ready.Error).IsNull(); await Assert.That(ready.Text).Contains("Add");
		}
		finally { Directory.Delete(directory, true); }
	}

	private static async Task Check(Type type)
	{
		await Assert.That(type.Namespace?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true).IsFalse();
		foreach (Type argument in type.GetGenericArguments()) await Check(argument);
	}
}