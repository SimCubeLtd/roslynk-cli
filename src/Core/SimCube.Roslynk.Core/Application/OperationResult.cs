namespace SimCube.Roslynk.Core.Application;

/// <summary>Owned application result. Text preserves source bodies and the established compact outline format.</summary>
public sealed record OperationResult(string Text, ApplicationError? Error = null)
{
	internal static OperationResult FromOutline(string text)
	{
		string[] headers = text.Split('\n');
		string? code = null;
		string message = "";
		var candidates = new List<string>();
		var staleFiles = new List<string>();
		foreach (string raw in headers)
		{
			string line = raw.TrimEnd('\r');
			if (line.Length == 0) break;
			if (line.StartsWith("error=")) code = line[6..];
			if (line.StartsWith("errorMessage=")) message = line[13..];
			if (line.StartsWith("candidate=")) candidates.Add(line[10..]);
			if (line.StartsWith("stale=")) staleFiles.Add(line[6..]);
		}
		return new(text, code is null ? null : new ApplicationError(code, message, candidates, staleFiles));
	}
}
public sealed record ApplicationError(string Code, string Message, IReadOnlyList<string> Candidates, IReadOnlyList<string> StaleFiles);
