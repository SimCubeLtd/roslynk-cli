using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Results;

namespace SimCube.Roslynk.Core.Infrastructure.Outlines;

/// <summary>
/// The one failure shape shared by every tool: a header-only block of 'error', 'errorMessage' (newline
/// sanitized), zero or more repeatable 'candidate' (name suggestions or ambiguous matches) and 'stale'
/// (files that moved on disk), then 'status'. Tools call this instead of hand-writing the error path, so
/// the format never drifts between them.
/// </summary>
internal static class OutlineError
{
	public static string Format(Error error, SolutionStatus status)
	{
		var builder = new OutlineBuilder();
		builder.Header("error", error.Code.ToString());
		builder.Header("errorMessage", error.Message);

		if (error.Candidates is not null)
		{
			foreach (string candidate in error.Candidates)
				builder.Header("candidate", candidate);
		}

		if (error.StaleFiles is not null)
		{
			foreach (string stale in error.StaleFiles)
				builder.Header("stale", stale);
		}

		builder.Status(status);
		return builder.ToString();
	}
}
