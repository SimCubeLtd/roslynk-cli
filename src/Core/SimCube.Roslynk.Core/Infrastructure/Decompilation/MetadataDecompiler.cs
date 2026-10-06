using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace SimCube.Roslynk.Core.Infrastructure.Decompilation;

/// <summary>A declaration reconstructed from IL, with the assembly file it was actually read from.</summary>
internal sealed record DecompiledSource(string AssemblyName, string AssemblyPath, string Text);

/// <summary>A symbol that exists in metadata but cannot be decompiled; the message is safe to show a caller.</summary>
internal sealed class DecompilationException(string message) : Exception(message);

/// <summary>
/// Reconstructs C# for symbols that only exist in referenced assemblies (NuGet packages, the BCL). Owned by one
/// <see cref="Lifecycle.RoslynInstance"/>, so its loaded assemblies are released when that solution is evicted
/// or closed. Decompilation is only ever done on explicit request; it is not part of normal body reads.
/// </summary>
internal sealed class MetadataDecompiler
{
	/// <summary>Each entry holds one assembly image plus the metadata of everything it references.</summary>
	private const int Capacity = 4;

	/// <summary>How many assemblies a forwarded type is followed through before giving up.</summary>
	private const int MaxForwards = 8;

	private readonly object Gate = new();
	private readonly List<(CacheKey Key, Lazy<DecompilerTypeSystem> TypeSystem)> Cache = [];

	/// <summary>
	/// Decompiles the declaration identified by <paramref name="documentationId"/> (a Roslyn documentation
	/// comment ID) from <paramref name="assemblyPath"/>. A reference assembly is swapped for its implementation
	/// first, and a type forwarded elsewhere is followed to the assembly that defines it.
	/// <paramref name="searchDirectories"/> are where the assembly's own references are looked up.
	/// </summary>
	public DecompiledSource Decompile(
		string assemblyPath,
		string documentationId,
		IReadOnlyCollection<string> searchDirectories,
		CancellationToken cancellationToken)
	{
		try
		{
			string path = ReferenceAssemblies.ResolveImplementation(assemblyPath);

			for (int forwards = 0; forwards <= MaxForwards; forwards++)
			{
				DecompilerTypeSystem typeSystem = TypeSystemFor(path, searchDirectories);

				IEntity entity = IdStringProvider.FindEntity(documentationId, new SimpleTypeResolveContext(typeSystem))
					?? throw new DecompilationException($"No declaration for it was found in '{Path.GetFileName(path)}'.");

				if (entity.ParentModule?.MetadataFile is not { } owner || owner == typeSystem.MainModule.MetadataFile)
				{
					var decompiler = new CSharpDecompiler(typeSystem, Settings()) { CancellationToken = cancellationToken };
					string text = decompiler.DecompileAsString(entity.MetadataToken).Trim();
					return new DecompiledSource(typeSystem.MainModule.AssemblyName, path, text);
				}

				// The assembly only forwards the type (System.Runtime to System.Private.CoreLib). The file that
				// defines it was found through the solution's reference directories, so it can itself be a
				// bodiless reference assembly. Its implementation is looked up again by ID, because a token is
				// only meaningful in the file it came from.
				path = ReferenceAssemblies.ResolveImplementation(owner.FileName);
			}

			throw new DecompilationException("Its type is forwarded through too many assemblies.");
		}
		catch (Exception exception) when (exception is not (OperationCanceledException or DecompilationException))
		{
			throw new DecompilationException($"Decompilation failed: {exception.Message}");
		}
	}

	/// <summary>Drops every cached assembly.</summary>
	public void Clear()
	{
		lock (Gate)
			Cache.Clear();
	}

	private DecompilerTypeSystem TypeSystemFor(string path, IReadOnlyCollection<string> searchDirectories)
	{
		// The write time is part of the key so a rebuilt local assembly is not served from a stale image.
		var key = new CacheKey(path, File.GetLastWriteTimeUtc(path));
		Lazy<DecompilerTypeSystem> entry;

		lock (Gate)
		{
			int index = Cache.FindIndex(candidate => candidate.Key == key);
			if (index >= 0)
			{
				entry = Cache[index].TypeSystem;
				Cache.RemoveAt(index);
			}
			else
			{
				entry = new Lazy<DecompilerTypeSystem>(() => Load(path, searchDirectories));
			}

			// Most recently used first; the least recently used entry falls off the end.
			Cache.Insert(0, (key, entry));
			if (Cache.Count > Capacity)
				Cache.RemoveAt(Cache.Count - 1);
		}

		try
		{
			return entry.Value;
		}
		catch
		{
			lock (Gate)
				Cache.RemoveAll(candidate => ReferenceEquals(candidate.TypeSystem, entry));
			throw;
		}
	}

	private static DecompilerTypeSystem Load(string path, IReadOnlyCollection<string> searchDirectories)
	{
		// Both images are read into memory and their files closed, so no handle is left on a package or
		// runtime assembly. Method bodies are only needed from the main file; references need metadata alone.
		var file = new PEFile(path, PEStreamOptions.PrefetchEntireImage);
		var resolver = new UniversalAssemblyResolver(
			path,
			throwOnError: false,
			file.DetectTargetFrameworkId(),
			file.DetectRuntimePack(),
			PEStreamOptions.PrefetchMetadata);

		foreach (string directory in searchDirectories)
			resolver.AddSearchDirectory(directory);

		return new DecompilerTypeSystem(file, resolver, Settings());
	}

	private static DecompilerSettings Settings() => new(LanguageVersion.Latest) { ThrowOnAssemblyResolveErrors = false };

	private readonly record struct CacheKey(string Path, DateTime WrittenUtc);
}
