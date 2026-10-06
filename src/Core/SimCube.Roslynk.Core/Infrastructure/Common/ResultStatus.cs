namespace SimCube.Roslynk.Core.Infrastructure.Common;

/// <summary>
/// The resolution outcome of a tool call: whether the target was answered, is genuinely
/// absent, or falls outside Roslynk's C#/solution scope.
/// </summary>
internal enum ResultStatus
{
	Ok,
	NotFound,
	NotSupported
}
