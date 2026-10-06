using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Diagnostics;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;

namespace SimCube.Roslynk.CoreTests.Infrastructure.Diagnostics;

public class DocumentDiagnosticsProviderTests
{
	[Test]
	public async Task WhenTheDocumentHasAnUnnecessaryUsing_ThenIde0005IsReturned() =>
		await WithGreeterAsync(async (document, subject) =>
		{
			ImmutableArray<Diagnostic> diagnostics = await subject.GetForDocumentAsync(document);

			await Assert.That(diagnostics).Contains(diagnostic => diagnostic.Id == "IDE0005");
		});

	[Test]
	public async Task WhenTheDocumentHasACompilerDiagnostic_ThenItIsStillReturned() =>
		await WithGreeterAsync(async (document, subject) =>
		{
			ImmutableArray<Diagnostic> diagnostics = await subject.GetForDocumentAsync(document);

			await Assert.That(diagnostics).Contains(diagnostic => diagnostic.Id == "CS8019");
		});

	[Test]
	public async Task WhenTheCaretIsElsewhereInTheFile_ThenTheUsingDiagnosticIsStillReturned() =>
		// IDE0005 reports on the using directive, so analysing only the span the caller asked about would
		// hide it whenever the caret sits in the body. The provider is deliberately whole-document.
		await WithGreeterAsync(async (document, subject) =>
		{
			SourceText text = await document.GetTextAsync();
			TextSpan body = CodeActionService.SpanFor(text, text.Lines.Count - 1, 1, null, null);

			Diagnostic? unnecessaryUsing = (await subject.GetForDocumentAsync(document))
				.FirstOrDefault(diagnostic => diagnostic.Id == "IDE0005");

			await Assert.That(unnecessaryUsing).IsNotNull();
			await Assert.That(unnecessaryUsing.Location.SourceSpan.IntersectsWith(body)).IsFalse().Because("The fixture's caret line should not cover the using.");
		});

	[Test]
	public async Task WhenTheSameDocumentIsRequestedTwice_ThenTheCachedResultIsReturned() =>
		await WithGreeterAsync(async (document, subject) =>
		{
			ImmutableArray<Diagnostic> first = await subject.GetForDocumentAsync(document);
			ImmutableArray<Diagnostic> second = await subject.GetForDocumentAsync(document);

			await Assert.That(first == second).IsTrue().Because("The second call should have returned the cached array.");
		});

	[Test]
	public async Task WhenTheDocumentIsEdited_ThenTheCacheIsNotReused() =>
		await WithGreeterAsync(async (document, subject) =>
		{
			ImmutableArray<Diagnostic> before = await subject.GetForDocumentAsync(document);

			Document edited = document.WithText(SourceText.From("namespace CodeStyleLibrary;\r\n\r\npublic class Greeter;\r\n"));
			ImmutableArray<Diagnostic> after = await subject.GetForDocumentAsync(edited);

			await Assert.That(before == after).IsFalse().Because("The edited document should not have hit the cache.");
			await Assert.That(after).DoesNotContain(diagnostic => diagnostic.Id == "IDE0005");
		});

	private static async Task WithGreeterAsync(Func<Document, DocumentDiagnosticsProvider, Task> body)
	{
		using var registry = new InstanceRegistry();
		RoslynInstance instance = await registry.GetOrAddAsync(TestSolutions.CodeStyle);
		Document document = CodeActionService.FindDocument(instance.CurrentSolution, "Greeter.cs")
			?? throw new InvalidOperationException("The CodeStyleSolution fixture no longer contains Greeter.cs.");

		await body(document, new DocumentDiagnosticsProvider());
	}
}