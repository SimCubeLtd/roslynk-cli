using SimCube.Roslynk.Core.Features.Symbols.GetMembers;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.Symbols.GetMembersTests;

public class GetMembersTests
{
	[Test]
	public async Task WhenATypesMembersAreRequested_ThenItsPublicMethodsAreReturned()
	{
		string result = await RunAsync("SimpleLibrary.Greeter");

		await Assert.That(result).Contains("resolvedType=SimpleLibrary.Greeter");
		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains("method,Greet");
	}

	[Test]
	public async Task WhenATypeHasPrivateMembers_ThenTheyAreIncluded()
	{
		string result = await RunAsync("SimpleLibrary.Holder");

		await Assert.That(result).Contains("field,_ready");
	}

	[Test]
	public async Task WhenAMetadataTypesMembersAreRequested_ThenTheyResolveUnderTheMetadataBucket()
	{
		string result = await RunAsync("System.String");

		await Assert.That(result).Contains("resolvedType=System.String");
		await Assert.That(result).Contains("<metadata>");
		await Assert.That(result).Contains("method,Substring");
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenIndexingIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new GetMembersTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.GetMembers(TestSolutions.Simple, "SimpleLibrary.Greeter");

		await Assert.That(result).Contains("error=Indexing");
		await Assert.That(result).Contains("status=Building");

		await registry.GetOrAddAsync(TestSolutions.Simple);
	}

	[Test]
	public async Task WhenANameFilterEndsWithAStar_ThenOnlyPrefixMatchesAreReturned()
	{
		string result = await RunAsync("System.String", nameFilter: "Sub*");

		await Assert.That(result).Contains("method,Substring");
		await Assert.That(MemberNames(result)).All(name => name.StartsWith("Sub", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public async Task WhenANameFilterHasNoStar_ThenItMatchesAsACaseInsensitiveSubstring()
	{
		string result = await RunAsync("System.String", nameFilter: "ubSTR");

		await Assert.That(result).Contains("method,Substring");
		await Assert.That(MemberNames(result)).All(name => name.Contains("ubstr", StringComparison.OrdinalIgnoreCase));
	}

	[Test]
	public async Task WhenMethodsAreExcluded_ThenNoMethodsAreReturnedButOtherKindsRemain()
	{
		string result = await RunAsync("System.String", includeMethods: false);

		await Assert.That(result).DoesNotContain("\tmethod,");
		await Assert.That(result).Contains("property,Length");
	}

	[Test]
	public async Task WhenOnlyFieldsAreRequested_ThenOtherKindsAreExcluded()
	{
		string result = await RunAsync(
			"System.String",
			includeMethods: false,
			includeProperties: false,
			includeEvents: false,
			includeNestedTypes: false);

		await Assert.That(result).Contains("field,Empty");
		await Assert.That(result).DoesNotContain("\tmethod,");
		await Assert.That(result).DoesNotContain("\tproperty,");
	}

	[Test]
	public async Task WhenANameFilterMatchesButItsKindIsExcluded_ThenItIsNotReturned()
	{
		string result = await RunAsync("System.String", nameFilter: "Length", includeProperties: false);

		// The Length property is gone; only the accessor method get_Length (a method) can still match.
		await Assert.That(MemberNames(result)).DoesNotContain("Length");
	}

	[Test]
	public async Task WhenNoFiltersAreGiven_ThenMethodsAndPropertiesAreBothReturned()
	{
		string result = await RunAsync("System.String");

		await Assert.That(result).Contains("\tmethod,");
		await Assert.That(result).Contains("\tproperty,");
	}

	[Test]
	public async Task WhenAMemberHasSource_ThenItIsGroupedUnderASolutionRelativeFileWithItsLoc()
	{
		string result = await RunAsync("SimpleLibrary.Greeter", nameFilter: "Greet");

		await Assert.That(result.Split('\n')).Contains(line => line == "SimpleLibrary");

		string fileLine = result.Split('\n').First(line => line.EndsWith("Greeter.cs", StringComparison.Ordinal));
		await Assert.That(Path.IsPathRooted(fileLine)).IsFalse().Because($"expected a solution-relative path, got '{fileLine}'");
		await Assert.That(fileLine).DoesNotContain('\\');

		string memberLine = result.Split('\n').First(line => line.TrimStart('\t').StartsWith("method,Greet", StringComparison.Ordinal));
		// kind,name,<loc>,<signature>: the third comma-field is the start:col-end:col span.
		string loc = memberLine.TrimStart('\t').Split(',')[2];
		await Assert.That(loc).Matches(@"^\d+:\d+-\d+:\d+$");
	}

	[Test]
	public async Task WhenAMemberComesFromMetadata_ThenItsLocFieldIsEmpty()
	{
		string result = await RunAsync("System.String", nameFilter: "Substring");

		await Assert.That(result).Contains("<metadata>");
		await Assert.That(result).DoesNotContain(".cs");
		// Empty loc field: kind,name,,<signature> (the doubled comma is the explicitly empty loc).
		await Assert.That(result).Contains("method,Substring,,");
	}

	[Test]
	public async Task WhenAMethodHasParameters_ThenTheSignatureIsAPipeDelimitedTypeList()
	{
		string result = await RunAsync("SimpleLibrary.Holder", nameFilter: "Combine");

		string memberLine = result.Split('\n').First(line => line.TrimStart('\t').StartsWith("method,Combine", StringComparison.Ordinal));
		// kind,name,<loc>,<paramType|paramType>
		string[] fields = memberLine.TrimStart('\t').Split(',');
		await Assert.That(fields[3]).IsEqualTo("string|string");
	}

	private static async Task<string> RunAsync(
		string typeName,
		bool includeInherited = false,
		string? nameFilter = null,
		bool includeMethods = true,
		bool includeFields = true,
		bool includeProperties = true,
		bool includeEvents = true,
		bool includeNestedTypes = true)
	{
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Simple);
		var subject = new GetMembersTool(registry, new SymbolResolver(), new ProjectionService());

		return await subject.GetMembers(
			TestSolutions.Simple,
			typeName,
			includeInherited,
			nameFilter,
			includeMethods,
			includeFields,
			includeProperties,
			includeEvents,
			includeNestedTypes);
	}

	private static IReadOnlyList<string> MemberNames(string text)
	{
		var names = new List<string>();
		foreach (string raw in text.Split('\n'))
		{
			if (!raw.StartsWith('\t'))
				continue;

			string line = raw.TrimStart('\t');
			int comma = line.IndexOf(',');
			if (comma < 0)
				continue;

			string rest = line[(comma + 1)..];
			int boundary = rest.IndexOfAny([',', ' ']);
			names.Add(boundary < 0 ? rest : rest[..boundary]);
		}

		return names;
	}
}