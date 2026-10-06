namespace SimCube.Roslynk.Core.Infrastructure.Patching;

/// <summary>One line of a hunk: its role and its text content with the leading marker stripped.</summary>
internal sealed class HunkLine
{
	public HunkLineKind Kind { get; }
	public string Text { get; }

	public HunkLine(HunkLineKind kind, string text)
	{
		Kind = kind;
		Text = text;
	}
}
