namespace SimCube.Roslynk.Cli;

internal static class SolutionDiscovery
{
	public static string Find(string? explicitPath, string currentDirectory)
	{
		if (explicitPath is not null)
		{
			string path = Path.GetFullPath(explicitPath, currentDirectory);
			if (!File.Exists(path)) throw new FileNotFoundException($"No solution at '{path}'.");
			return path;
		}
		for (DirectoryInfo? directory = new(currentDirectory); directory is not null; directory = directory.Parent)
		{
			string[] candidates = Candidates(directory.FullName);
			// Common repository layouts keep solutions in src or Source, beside the root documentation.
			if (candidates.Length == 0)
				candidates = new[] { "src", "Source" }
					.Select(name => Path.Combine(directory.FullName, name))
					.Where(Directory.Exists)
					.SelectMany(Candidates)
					.Distinct(StringComparer.Ordinal)
					.Order(StringComparer.Ordinal)
					.ToArray();
			if (candidates.Length == 1) return candidates[0];
			if (candidates.Length > 1) throw new ArgumentException("Multiple solutions found:\n" + string.Join('\n', candidates.Select(path => Path.GetRelativePath(currentDirectory, path))) + "\nSpecify one with --solution <path>.");
		}
		throw new FileNotFoundException("No .slnx or .sln found. Specify one with --solution <path>.");
	}
	private static string[] Candidates(string directory) => Directory.EnumerateFiles(directory).Where(path => Path.GetExtension(path) is ".slnx" or ".sln").Order(StringComparer.Ordinal).ToArray();
}
