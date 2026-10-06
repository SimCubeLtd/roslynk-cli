using TUnit.Assertions.Enums;
using SimCube.Roslynk.Core.Application;
using SimCube.Roslynk.Protocol;
using System.Reflection;

namespace SimCube.Roslynk.TransportTests;

public sealed class CapabilityTests
{
	[Test]
	public async Task WhenOriginalCapabilitiesAreInventoried_ThenEachHasATypedCoreAndProtocolContract()
	{
		string[] capabilities = ["GetCallers", "ApplyCodeAction", "ApplyCodeFix", "GetCodeActions", "FindDeadConditionals", "FindDeadCode", "GetDiagnostics", "MultiQuery", "ApplyPatch", "ExtractMethod", "FindReads", "FindReferences", "FindWrites", "RenameSymbol", "ChangeSignature", "RenameParameter", "GetSolutionStatus", "OpenSolution", "ReloadSolution", "FindDefinition", "FindImplementations", "GetExpressionInfo", "GetMembers", "GetSymbol", "GetSymbolBody", "GetTypeHierarchy", "SearchSymbols", "RemoveUnusedUsings"];
		await Assert.That(capabilities.Length).IsEqualTo(28);
		await Assert.That(Enum.GetNames<RequestKind>().Where(name => (ushort)Enum.Parse<RequestKind>(name) >= 10).Order()).IsEquivalentTo(capabilities.Order(), CollectionOrdering.Matching);
		foreach (string name in capabilities)
		{
			Type protocol = typeof(Wire).Assembly.GetType("SimCube.Roslynk.Protocol." + name + "Request")!;
			Type domain = typeof(RoslynkApplication).Assembly.GetType("SimCube.Roslynk.Core.Application." + name + "Request")!;
			MethodInfo method = typeof(RoslynkApplication).GetMethod(name + "Async")!;
			await Assert.That(protocol).IsNotNull(); await Assert.That(domain).IsNotNull(); await Assert.That(method).IsNotNull();
			await Assert.That(method.GetParameters()[0].ParameterType).IsEqualTo(domain);
			await Assert.That(method.ReturnType).IsEqualTo(typeof(Task<OperationResult>));
			await Assert.That(protocol.GetProperties().Select(property => property.Name).Order()).IsEquivalentTo(domain.GetProperties().Select(property => property.Name).Order(), CollectionOrdering.Matching);
			ParameterInfo[] domainParameters = domain.GetConstructors()[0].GetParameters();
			ParameterInfo[] wireParameters = protocol.GetConstructors()[0].GetParameters();
			await Assert.That(wireParameters.Select(parameter => parameter.DefaultValue)).IsEquivalentTo(domainParameters.Select(parameter => parameter.DefaultValue), CollectionOrdering.Matching);
		}
	}
}
