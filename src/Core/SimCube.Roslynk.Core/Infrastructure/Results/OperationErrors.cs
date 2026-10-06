using System.Text.Json;
using SimCube.Roslynk.Core.Infrastructure.Writing;
namespace SimCube.Roslynk.Core.Infrastructure.Results;

internal static class OperationErrors
{
	public static Error ToError(Exception exception) => exception switch {
		StaleWriteException stale => Error.Stale(stale.Message, [stale.FilePath]),
		FileNotFoundException missing => Error.NotFound(missing.Message),
		ArgumentException or FormatException or JsonException or NotSupportedException => Error.Invalid(exception.Message),
		_ => Error.Faulted(exception.Message)
	};
}
