using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SimCube.Roslynk.Core.Infrastructure.Decompilation;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Decompilation;

public class MetadataDecompilerTests
{
	[Test]
	public async Task WhenAForwardedTypeResolvesToAReferenceAssembly_ThenItsImplementationIsDecompiled()
	{
		// A facade forwards Forwarded.Widget to Library, which the solution only references through the
		// package's bodiless ref/<tfm> assembly. The body lives in the lib/<tfm> assembly beside it.
		string root = Path.Combine(Path.GetTempPath(), "rk-decompile-" + Guid.NewGuid().ToString("N"));
		try
		{
			string reference = Path.Combine(root, "library", "1.0.0", "ref", "net10.0", "Library.dll");
			string implementation = Path.Combine(root, "library", "1.0.0", "lib", "net10.0", "Library.dll");
			string facade = Path.Combine(root, "facade", "Facade.dll");

			Emit(implementation, "Library", "namespace Forwarded; public class Widget { public int Answer() { return 42; } }");
			Emit(reference, "Library", "[assembly: System.Runtime.CompilerServices.ReferenceAssembly] namespace Forwarded; public class Widget { public int Answer() { throw null!; } }");
			Emit(facade, "Facade", "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Forwarded.Widget))]", reference);

			DecompiledSource result = new MetadataDecompiler().Decompile(
				facade,
				"M:Forwarded.Widget.Answer",
				[Path.GetDirectoryName(reference)!, Path.GetDirectoryName(typeof(object).Assembly.Location)!],
				CancellationToken.None);

			await Assert.That(result.AssemblyPath).IsEqualTo(implementation);
			await Assert.That(result.Text).Contains("return 42;");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	[Test]
	public async Task WhenTheSearchDirectoriesChange_ThenACachedAssemblyIsNotReusedWithTheOldOnes()
	{
		// The same facade is decompiled twice by one decompiler. Only the directory its dependency is
		// resolved from changes, as when a rebuilt solution references a different version of it.
		string root = Path.Combine(Path.GetTempPath(), "rk-decompile-" + Guid.NewGuid().ToString("N"));
		try
		{
			string first = Path.Combine(root, "first", "Library.dll");
			string second = Path.Combine(root, "second", "Library.dll");
			string facade = Path.Combine(root, "facade", "Facade.dll");
			string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

			Emit(first, "Library", "namespace Forwarded; public class Widget { public int Answer() { return 1; } }");
			Emit(second, "Library", "namespace Forwarded; public class Widget { public int Answer() { return 2; } }");
			Emit(facade, "Facade", "[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(Forwarded.Widget))]", first);

			var subject = new MetadataDecompiler();
			DecompiledSource before = subject.Decompile(facade, "M:Forwarded.Widget.Answer", [Path.GetDirectoryName(first)!, runtime], CancellationToken.None);
			DecompiledSource after = subject.Decompile(facade, "M:Forwarded.Widget.Answer", [Path.GetDirectoryName(second)!, runtime], CancellationToken.None);

			await Assert.That(before.AssemblyPath).IsEqualTo(first);
			await Assert.That(after.AssemblyPath).IsEqualTo(second);
			await Assert.That(after.Text).Contains("return 2;");
		}
		finally
		{
			Directory.Delete(root, true);
		}
	}

	private static void Emit(string path, string assemblyName, string source, params string[] references)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		CSharpCompilation compilation = CSharpCompilation.Create(
			assemblyName,
			[CSharpSyntaxTree.ParseText(source)],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location), .. references.Select(reference => MetadataReference.CreateFromFile(reference))],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var result = compilation.Emit(path);
		if (!result.Success)
			throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
	}
}
