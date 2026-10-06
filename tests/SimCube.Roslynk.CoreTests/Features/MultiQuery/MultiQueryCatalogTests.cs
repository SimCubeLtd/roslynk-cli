using TUnit.Assertions.Enums;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Features.MultiQuery;

namespace SimCube.Roslynk.CoreTests.Features.MultiQuery;

/// <summary>
/// The catalog guard: pins catalog, enum, tool constants, core signatures and the published schema to each
/// other, so drift between them fails here rather than in an agent transcript.
/// </summary>
public class MultiQueryCatalogTests
{
	[Test]
	public async Task WhenTheCatalogIsEnumerated_ThenItHoldsExactlyTheFourteenQueryTools()
	{
		string[] expected =
		[
			"get_symbol",
			"get_symbol_body",
			"get_members",
			"find_definition",
			"find_implementations",
			"find_references",
			"get_callers",
			"search_symbols",
			"get_type_hierarchy",
			"find_dead_code",
			"find_dead_conditionals",
			"get_expression_info",
			"find_reads",
			"find_writes",
		];

		await Assert.That(MultiQueryCatalog.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray()).IsEquivalentTo(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);
	}

	[Test]
	public async Task WhenTheEnumIsEnumerated_ThenItsMembersMatchTheCatalogExactly()
	{
		string[] enumNames = Enum.GetNames<MultiQueryOp>().OrderBy(name => name, StringComparer.Ordinal).ToArray();
		string[] catalogNames = MultiQueryCatalog.Entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

		await Assert.That(enumNames).IsEquivalentTo(catalogNames, CollectionOrdering.Matching);
	}

	[Test]
	public async Task WhenAToolGainsAMultiQueryableCore_ThenTheCoreResolvesWithThePinnedParameters()
	{
		foreach ((string toolName, MultiQueryCatalog.OpEntry entry) in MultiQueryCatalog.Entries)
		{
			MethodInfo core = entry.Resolve();

			// The seam: every core takes the pinned model and its instance, and never acquires either.
			ParameterInfo[] parameters = core.GetParameters();
			await Assert.That(parameters.FirstOrDefault(p => p.ParameterType == typeof(SimCube.Roslynk.Core.Infrastructure.Lifecycle.SolutionModel)) is not null).IsTrue().Because($"{toolName}: core '{core.Name}' does not declare a SolutionModel parameter.");
			await Assert.That(parameters.FirstOrDefault(p => p.ParameterType == typeof(SimCube.Roslynk.Core.Infrastructure.Lifecycle.RoslynInstance)) is not null).IsTrue().Because($"{toolName}: core '{core.Name}' does not declare a RoslynInstance parameter.");
			await Assert.That(parameters.Last().ParameterType == typeof(CancellationToken)).IsTrue().Because($"{toolName}: core '{core.Name}' does not end with a CancellationToken.");
		}
	}

	[Test]
	public async Task WhenAnEnumMemberIsRenamedOrAToolConstantMoves_ThenTheCatalogAndTheConstantsStillAgree()
	{
		// The enum member names must equal each tool's published name constant, so a rename that misses one
		// side fails here.
		foreach ((string toolName, MultiQueryCatalog.OpEntry entry) in MultiQueryCatalog.Entries)
		{
			object? constant = entry.ToolType
				.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
				.FirstOrDefault(field => field.IsLiteral && field.FieldType == typeof(string) && (string?)field.GetRawConstantValue() == toolName)
				?.GetRawConstantValue();

			await Assert.That(constant is string).IsTrue().Because($"'{toolName}' is in the catalog but no public const string equals it on {entry.ToolType.Name}.");
		}
	}
}
