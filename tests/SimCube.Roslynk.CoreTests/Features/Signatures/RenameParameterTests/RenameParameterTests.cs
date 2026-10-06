using System.IO;
using SimCube.Roslynk.Core.Features.Signatures.RenameParameter;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.Signatures.RenameParameterTests;

public class RenameParameterTests
{
	private const string ExtraSource =
		"""
		namespace SimpleLibrary;

		public interface IShape
		{
			int Area(int size);
		}

		public class Square : IShape
		{
			public int Area(int side) => side * side;
		}

		public class Box
		{
			public Box(int width)
			{
				Width = width;
			}

			public int Width { get; }
		}

		public class NamedCaller
		{
			public int Run() => new Calculator().Add(a: 1, b: 2) + new Box(width: 3).Width;
		}
		""";

	[Test]
	public async Task WhenRenamingAParameterOfOneOverload_ThenItsDeclarationBodyAndParamRefChangeButTheOtherOverloadDoesNot()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add(int)", "amount", "value");

			await Assert.That(result).Contains("applied=Y");
			await Assert.That(result).Contains("resolvedMethod=SimpleLibrary.Ledger.Add(int)");
			await Assert.That(result).Contains("parameter=amount");
			await Assert.That(result).Contains("renamedMembers=1");
			await Assert.That(result).Contains("Ledger.cs");
			string text = await File.ReadAllTextAsync(FindFile(solutionPath, "Ledger.cs"));
			await Assert.That(text).Contains("<paramref name=\"value\"/>");
			await Assert.That(text).Contains("public int Add(int value)");
			await Assert.That(text).Contains("Total += value;");
			await Assert.That(text).Contains("public int Add(int amount, int times)");
			await Assert.That(text).Contains("Add(amount);");
		}
	}

	[Test]
	public async Task WhenRenamingAnInterfaceMemberParameter_ThenTheImplementationIsRenamedToo()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.IGreeter.Greet", "name", "recipient");

			await Assert.That(result).Contains("applied=Y");
			await Assert.That(result).Contains("renamedMembers=2");
			await Assert.That(result).DoesNotContain("unchangedRelated");
			string contract = await File.ReadAllTextAsync(FindFile(solutionPath, "IGreeter.cs"));
			await Assert.That(contract).Contains("<paramref name=\"recipient\"/>");
			await Assert.That(contract).Contains("<param name=\"recipient\">");
			await Assert.That(contract).Contains("string Greet(string recipient);");
			string implementation = await File.ReadAllTextAsync(FindFile(solutionPath, "Greeter.cs"));
			await Assert.That(implementation).Contains("Greet(string recipient) => $\"Hello, {recipient}!\"");
		}
	}

	[Test]
	public async Task WhenRenamingAnImplementationParameter_ThenTheInterfaceMemberIsRenamedToo()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Greeter.Greet", "name", "recipient");

			await Assert.That(result).Contains("renamedMembers=2");
			await Assert.That(await File.ReadAllTextAsync(FindFile(solutionPath, "IGreeter.cs"))).Contains("string Greet(string recipient);");
		}
	}

	[Test]
	public async Task WhenARelatedDeclarationUsesADifferentName_ThenItIsLeftAloneAndReported()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.IShape.Area", "size", "length");

			await Assert.That(result).Contains("applied=Y");
			await Assert.That(result).Contains("renamedMembers=1");
			await Assert.That(result).Contains("unchangedRelated=SimpleLibrary.Square.Area");
			string text = await File.ReadAllTextAsync(FindFile(solutionPath, "Extra.cs"));
			await Assert.That(text).Contains("int Area(int length);");
			await Assert.That(text).Contains("public int Area(int side) => side * side;");
		}
	}

	[Test]
	public async Task WhenAParameterIsPassedAsANamedArgument_ThenTheCallSiteIsUpdated()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Calculator.Add", "a", "left");

			await Assert.That(result).Contains("applied=Y");
			await Assert.That(await File.ReadAllTextAsync(FindFile(solutionPath, "Calculator.cs"))).Contains("return left + b;");
			await Assert.That(await File.ReadAllTextAsync(FindFile(solutionPath, "Extra.cs"))).Contains("Add(left: 1, b: 2)");
		}
	}

	[Test]
	public async Task WhenRenamingAConstructorParameter_ThenTheConstructorAndNamedArgumentAreUpdated()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Box.Box(int)", "width", "size");

			await Assert.That(result).Contains("applied=Y");
			string text = await File.ReadAllTextAsync(FindFile(solutionPath, "Extra.cs"));
			await Assert.That(text).Contains("public Box(int size)");
			await Assert.That(text).Contains("Width = size;");
			await Assert.That(text).Contains("new Box(size: 3)");
		}
	}

	[Test]
	public async Task WhenCheckOnly_ThenTheChangedFilesAreListedAndNothingIsWritten()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string greeter = FindFile(solutionPath, "Greeter.cs");
			string before = await File.ReadAllTextAsync(greeter);

			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.IGreeter.Greet", "name", "recipient", checkOnly: true);

			await Assert.That(result).Contains("applied=N");
			await Assert.That(result).Contains("IGreeter.cs");
			await Assert.That(result).Contains("Greeter.cs");
			await Assert.That(await File.ReadAllTextAsync(greeter)).IsEqualTo(before);
		}
	}

	[Test]
	public async Task WhenTheNewNameIsAnotherParameter_ThenItIsAConflictAndNothingIsWritten()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string ledger = FindFile(solutionPath, "Ledger.cs");
			string before = await File.ReadAllTextAsync(ledger);

			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add(int, int)", "amount", "times");

			await Assert.That(result).Contains("error=Conflict");
			await Assert.That(result).Contains("a parameter named 'times'");
			await Assert.That(await File.ReadAllTextAsync(ledger)).IsEqualTo(before);
		}
	}

	[Test]
	public async Task WhenTheNewNameIsALocal_ThenItIsAConflict()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add(int, int)", "times", "index");

			await Assert.That(result).Contains("error=Conflict");
			await Assert.That(result).Contains("a local variable named 'index'");
		}
	}

	[Test]
	public async Task WhenTheParameterDoesNotExist_ThenItIsNotFoundListingTheParameters()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add(int, int)", "count", "total");

			await Assert.That(result).Contains("error=NotFound");
			await Assert.That(result).Contains("amount, times");
		}
	}

	[Test]
	public async Task WhenTheMethodNameIsAmbiguous_ThenEachOverloadIsACandidate()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add", "amount", "value");

			await Assert.That(result).Contains("error=Ambiguous");
			await Assert.That(result).Contains("SimpleLibrary.Ledger.Add(int)");
			await Assert.That(result).Contains("SimpleLibrary.Ledger.Add(int, int)");
		}
	}

	[Test]
	public async Task WhenTheSymbolIsNotAMethod_ThenItIsNotSupported()
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Total", "value", "amount");

			await Assert.That(result).Contains("error=NotSupported");
		}
	}

	[Test]
	[Arguments("1bad")]
	[Arguments("amount")]
	public async Task WhenTheNewNameIsInvalidOrUnchanged_ThenItIsInvalid(string newName)
	{
		(string solutionPath, RenameParameterTool subject, InstanceRegistry registry) = await CreateAsync();
		using (registry)
		{
			string result = await subject.RenameParameter(solutionPath, "SimpleLibrary.Ledger.Add(int)", "amount", newName);

			await Assert.That(result).Contains("error=Invalid");
		}
	}

	private static async Task<(string SolutionPath, RenameParameterTool Subject, InstanceRegistry Registry)> CreateAsync()
	{
		string solutionPath = TestSolutions.CreateScratchSimpleSolution();
		await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(FindFile(solutionPath, "Ledger.cs"))!, "Extra.cs"), ExtraSource);
		var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameParameterTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());
		return (solutionPath, subject, registry);
	}

	private static string FindFile(string solutionPath, string fileName) =>
		Directory.GetFiles(Path.GetDirectoryName(solutionPath)!, fileName, SearchOption.AllDirectories)
			.Single(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
				&& !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
}