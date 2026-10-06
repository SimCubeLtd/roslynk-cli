using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SimCube.Roslynk.Core.Infrastructure.Decompilation;

/// <summary>
/// Compilations reference the BCL (and some packages) through reference assemblies, which declare the API but
/// carry no method bodies. This maps one to the implementation assembly that does.
/// </summary>
internal static class ReferenceAssemblies
{
	private const string ReferenceAssemblyAttribute = "ReferenceAssemblyAttribute";
	private const string ReferencePackSuffix = ".Ref";

	/// <summary>
	/// Returns <paramref name="assemblyPath"/> itself when it has bodies, otherwise its implementation. Throws
	/// <see cref="DecompilationException"/> when it is a reference assembly with no implementation on this machine.
	/// </summary>
	public static string ResolveImplementation(string assemblyPath)
	{
		if (!IsReferenceAssembly(assemblyPath))
			return assemblyPath;

		return FindImplementation(assemblyPath)
			?? throw new DecompilationException(
				$"'{Path.GetFileName(assemblyPath)}' is a reference assembly with no method bodies, and no implementation assembly was found for it.");
	}

	internal static bool IsReferenceAssembly(string assemblyPath)
	{
		using var stream = File.OpenRead(assemblyPath);
		using var image = new PEReader(stream);
		MetadataReader reader = image.GetMetadataReader();

		foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
		{
			EntityHandle constructor = reader.GetCustomAttribute(handle).Constructor;
			StringHandle name = constructor.Kind switch
			{
				HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent
					=> reader.GetTypeReference((TypeReferenceHandle)parent).Name,
				HandleKind.MethodDefinition
					=> reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()).Name,
				_ => default
			};

			if (!name.IsNil && reader.StringComparer.Equals(name, ReferenceAssemblyAttribute))
				return true;
		}

		return false;
	}

	/// <summary>
	/// Understands the two layouts a reference assembly ships in: a package's <c>ref/&lt;tfm&gt;</c> beside its
	/// <c>lib/&lt;tfm&gt;</c>, and a targeting pack (<c>packs/&lt;Name&gt;.Ref/&lt;version&gt;/ref/&lt;tfm&gt;</c>, or the
	/// same pack restored into the NuGet cache) whose implementation is the shared runtime <c>&lt;Name&gt;</c>.
	/// </summary>
	internal static string? FindImplementation(string referencePath)
	{
		string fileName = Path.GetFileName(referencePath);
		string? frameworkDirectory = Path.GetDirectoryName(referencePath);
		string? refDirectory = Path.GetDirectoryName(frameworkDirectory);
		string? versionDirectory = Path.GetDirectoryName(refDirectory);
		string? packDirectory = Path.GetDirectoryName(versionDirectory);
		if (packDirectory is null || !string.Equals(Path.GetFileName(refDirectory), "ref", StringComparison.OrdinalIgnoreCase))
			return null;

		string library = Path.Combine(versionDirectory!, "lib", Path.GetFileName(frameworkDirectory)!, fileName);
		if (File.Exists(library))
			return library;

		string packName = Path.GetFileName(packDirectory);
		if (!packName.EndsWith(ReferencePackSuffix, StringComparison.OrdinalIgnoreCase))
			return null;

		string runtimeName = packName[..^ReferencePackSuffix.Length];
		foreach (string sharedRoot in SharedRoots(packDirectory))
		{
			// The NuGet cache lower-cases the pack name, so the runtime folder is matched without case.
			string? runtime = Directory.Exists(sharedRoot)
				? Directory.EnumerateDirectories(sharedRoot).FirstOrDefault(directory => string.Equals(Path.GetFileName(directory), runtimeName, StringComparison.OrdinalIgnoreCase))
				: null;
			if (runtime is null)
				continue;

			string? implementation = BestVersion(runtime, Path.GetFileName(versionDirectory!))
				.Select(directory => Path.Combine(directory, fileName))
				.FirstOrDefault(File.Exists);
			if (implementation is not null)
				return implementation;
		}

		return null;
	}

	/// <summary>The <c>shared</c> folder beside the pack's own <c>packs</c> folder, then the one this process runs on.</summary>
	private static IEnumerable<string> SharedRoots(string packDirectory)
	{
		string? packs = Path.GetDirectoryName(packDirectory);
		if (packs is not null && Path.GetDirectoryName(packs) is { } dotnetRoot && string.Equals(Path.GetFileName(packs), "packs", StringComparison.OrdinalIgnoreCase))
			yield return Path.Combine(dotnetRoot, "shared");

		// <root>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
		string? running = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(object).Assembly.Location)));
		if (running is not null)
			yield return running;
	}

	/// <summary>
	/// The runtime version matching the pack exactly, then the newest with the same major version (a targeting
	/// pack usually trails the installed runtime by patch releases), then the newest of any version. A project
	/// can target a framework whose runtime is not installed; the result names the file actually read, so a
	/// different runtime version is visible to the caller.
	/// </summary>
	private static IEnumerable<string> BestVersion(string runtimeDirectory, string packVersion)
	{
		string exact = Path.Combine(runtimeDirectory, packVersion);
		if (Directory.Exists(exact))
			yield return exact;

		if (!TryParse(packVersion, out Version? wanted))
			yield break;

		IEnumerable<string> installed = Directory.EnumerateDirectories(runtimeDirectory)
			.Select(directory => (Directory: directory, Parsed: TryParse(Path.GetFileName(directory), out Version? version) ? version : null))
			.Where(candidate => candidate.Parsed is not null)
			.OrderByDescending(candidate => candidate.Parsed!.Major == wanted.Major)
			.ThenByDescending(candidate => candidate.Parsed)
			.Select(candidate => candidate.Directory);

		foreach (string directory in installed)
			yield return directory;
	}

	private static bool TryParse(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Version? version)
	{
		// "10.0.0-rc.1.25451.107" compares on its numeric part.
		int suffix = text.IndexOf('-');
		return Version.TryParse(suffix < 0 ? text : text[..suffix], out version);
	}
}
