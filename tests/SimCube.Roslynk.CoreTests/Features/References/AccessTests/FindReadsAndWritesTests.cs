using SimCube.Roslynk.Core.Features.References.FindReads;
using SimCube.Roslynk.Core.Features.References.FindWrites;
using SimCube.Roslynk.Core.Infrastructure.Accesses;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;

namespace SimCube.Roslynk.CoreTests.Features.References.AccessTests;

public class FindReadsAndWritesTests
{
	private const string Mutate = "\t\t\t\t\tmethod,Mutate,";

	private static async Task<(FindReadsTool Reads, FindWritesTool Writes, InstanceRegistry Registry)> CreateAsync()
	{
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(TestSolutions.Access);
		return (
			new FindReadsTool(registry, new SymbolResolver(), new ProjectionService()),
			new FindWritesTool(registry, new SymbolResolver(), new ProjectionService()),
			registry);
	}

	private static string[] Lines(string result) => result.Split('\n');

	[Test]
	public async Task WhenWritesToAFieldAreRequested_ThenEachWriteFormIsClassified()
	{
		(_, FindWritesTool writes, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await writes.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Total");

			await Assert.That(result).Contains("resolvedSymbol=AccessSpace.Counter.Total");
			await Assert.That(Lines(result)).Contains("\t\t\t\t\tfield,Total,5:13,init");
			await Assert.That(Lines(result)).Contains("\t\t\t\t\tmethod,.ctor,11:3,init");
			await Assert.That(Lines(result)).Contains(Mutate + "18:3,assign");
			await Assert.That(Lines(result)).Contains(Mutate + "19:3,compound");
			await Assert.That(Lines(result)).Contains(Mutate + "20:3,increment");
			await Assert.That(Lines(result)).Contains(Mutate + "21:12,ref");
			await Assert.That(Lines(result)).Contains(Mutate + "22:11,out");
			await Assert.That(Lines(result)).Contains(Mutate + "23:4,assign");
			await Assert.That(Lines(result)).Contains(Mutate + "30:31,assign");
			await Assert.That(result).DoesNotContain(",read");
			await Assert.That(result).DoesNotContain("24:");
			await Assert.That(result).DoesNotContain("25:");
			await Assert.That(result).DoesNotContain("class,Other");
		}
	}

	[Test]
	public async Task WhenReadsOfAFieldAreRequested_ThenDualAccessesAppearWithTheirOwnKind()
	{
		(FindReadsTool reads, _, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Total");

			await Assert.That(Lines(result)).Contains(Mutate + "17:14,read");
			await Assert.That(Lines(result)).Contains(Mutate + "19:3,compound");
			await Assert.That(Lines(result)).Contains(Mutate + "20:3,increment");
			await Assert.That(Lines(result)).Contains(Mutate + "21:12,ref");
			await Assert.That(Lines(result)).Contains(Mutate + "23:26,read");
			await Assert.That(result).DoesNotContain(",assign");
			await Assert.That(result).DoesNotContain(",out");
			await Assert.That(result).DoesNotContain(",init");
			await Assert.That(result).DoesNotContain("25:");
		}
	}

	[Test]
	public async Task WhenAPropertyIsWrittenInItsConstructorAndAnInitialiser_ThenBothAreInit()
	{
		(_, FindWritesTool writes, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string count = await writes.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Count");
			string name = await writes.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Name");

			await Assert.That(Lines(count)).Contains("\t\t\t\t\tmethod,.ctor,12:8,init");
			await Assert.That(Lines(name)).Contains("\t\t\t\t\tmethod,Make,35:41,init");
		}
	}

	[Test]
	public async Task WhenAParameterIsAddressedByMemberColonName_ThenOnlyItsAccessesAreReported()
	{
		(FindReadsTool reads, FindWritesTool writes, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string written = await writes.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Mutate:amount");
			string read = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Mutate(int, AccessSpace.Other):amount");
			string namedArgumentOnly = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Use:value");

			await Assert.That(written).Contains("resolvedSymbol=AccessSpace.Counter.Mutate(int, Other):amount");
			await Assert.That(Lines(written)).Contains(Mutate + "26:3,assign");
			await Assert.That(Lines(written)).Contains(Mutate + "27:3,compound");
			await Assert.That(Lines(read)).Contains(Mutate + "18:11,read");
			await Assert.That(Lines(read)).Contains(Mutate + "28:7,read");
			await Assert.That(namedArgumentOnly).DoesNotContain("29:");
			await Assert.That(namedArgumentOnly).Contains("method,Use,33:36,read");
		}
	}

	[Test]
	public async Task WhenALocalVariableIsRequested_ThenNotSupportedIsReturned()
	{
		(FindReadsTool reads, _, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Mutate:copy");

			await Assert.That(result).Contains("error=NotSupported");
		}
	}

	[Test]
	public async Task WhenTheSymbolCannotBeReadOrWritten_ThenNotSupportedIsReturned()
	{
		(FindReadsTool reads, _, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Make");

			await Assert.That(result).Contains("error=NotSupported");
		}
	}

	[Test]
	public async Task WhenTheParameterDoesNotExist_ThenNotFoundListsTheRealParameters()
	{
		(FindReadsTool reads, _, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Mutate:nope");

			await Assert.That(result).Contains("error=NotFound");
			await Assert.That(result).Contains("AccessSpace.Counter.Mutate(int, Other):amount");
		}
	}

	[Test]
	public async Task WhenAnOverloadedOrMissingSymbolIsRequested_ThenTheResolverErrorsAreReturned()
	{
		(FindReadsTool reads, _, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			await Assert.That(await reads.FindReads(TestSolutions.Access, "AccessSpace.Counter.Missing")).Contains("error=NotFound");
		}
	}

	[Test]
	public async Task WhenMoreAccessesMatchThanMaxResults_ThenTheHeaderReportsTruncated()
	{
		(_, FindWritesTool writes, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await writes.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Total", maxResults: 2);

			await Assert.That(result).Contains("truncated=Y");
			await Assert.That(result).Contains("count=9");
			await Assert.That(Lines(result).Count(line => line.EndsWith(",init") || line.EndsWith(",assign"))).IsEqualTo(2);
		}
	}

	[Test]
	public async Task WhenTheSolutionIsStillLoading_ThenAnIndexingHeaderIsReturned()
	{
		using var registry = new InstanceRegistry();
		var subject = new FindWritesTool(registry, new SymbolResolver(), new ProjectionService());

		string result = await subject.FindWrites(TestSolutions.Access, "AccessSpace.Counter.Total");

		await Assert.That(result).Contains("error=Indexing");
		await registry.GetOrAddAsync(TestSolutions.Access);
	}

	[Test]
	[Arguments("N.T.M:p", "N.T.M", "p")]
	[Arguments("N.T.M(int, string):p", "N.T.M(int, string)", "p")]
	[Arguments("N.T.M(global::X.Y):p", "N.T.M(global::X.Y)", "p")]
	[Arguments("N.T.F", "N.T.F", null)]
	[Arguments("N.T.M(global::X.Y)", "N.T.M(global::X.Y)", null)]
	public async Task WhenANameIsSplit_ThenTheParameterFollowsTheLastSingleColon(string input, string member, string? parameter)
	{
		await Assert.That(AccessQuery.Split(input)).IsEqualTo((member, parameter));
	}
}