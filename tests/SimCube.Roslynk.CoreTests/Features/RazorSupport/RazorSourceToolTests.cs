using System.IO;
using SimCube.Roslynk.Core.Features.CodeActions.ApplyCodeAction;
using SimCube.Roslynk.Core.Features.CodeActions.GetCodeActions;
using SimCube.Roslynk.Core.Features.Patching.ApplyPatch;
using SimCube.Roslynk.Core.Features.References.RenameSymbol;
using SimCube.Roslynk.Core.Features.Refactorings.ExtractMethod;
using SimCube.Roslynk.Core.Features.Signatures.ChangeSignature;
using SimCube.Roslynk.Core.Features.Signatures.RenameParameter;
using SimCube.Roslynk.Core.Infrastructure.CodeActions;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Projections;
using SimCube.Roslynk.Core.Infrastructure.Resolution;
using SimCube.Roslynk.Core.Infrastructure.Writing;

namespace SimCube.Roslynk.CoreTests.Features.RazorSupport;

/// <summary>
/// Every tool that takes a document path or edits code works on .razor and .cshtml sources: positions are
/// given in the Razor file, Roslyn runs on the generated C#, and the edits are written to the Razor file.
/// </summary>
public class RazorSourceToolTests
{
	private const string CounterRelativePath = "RazorLib/Counter.razor";
	private const string IndexRelativePath = "CshtmlLib/Views/Home/Index.cshtml";
	private const string IndexTypeName = "AspNetCoreGeneratedDocument.Views_Home_Index";

	// ---- get_code_actions / apply_code_action ----

	[Test]
	public async Task WhenListingActionsInsideARazorCodeBlock_ThenTheCompilerFixIsOffered()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "unused");

		string result = await subject.GetCodeActions(solutionPath, CounterRelativePath, line, column);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains(",Fix,CS0219 ");
	}

	[Test]
	public async Task WhenListingActionsInsideACshtmlFunctionsBlock_ThenTheCompilerFixIsOffered()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "unused");

		string result = await subject.GetCodeActions(solutionPath, IndexRelativePath, line, column);

		await Assert.That(result).DoesNotContain("error=");
		await Assert.That(result).Contains(",Fix,CS0219 ");
	}

	[Test]
	public async Task WhenListingActionsOnRazorMarkup_ThenItIsNotSupported()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new GetCodeActionsTool(registry, TestServices.CodeActions());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "<p>Starting");

		string result = await subject.GetCodeActions(solutionPath, CounterRelativePath, line, column);

		await Assert.That(result).Contains("error=NotSupported");
	}

	[Test]
	public async Task WhenApplyingAnActionDiscoveredInARazorFile_ThenTheRazorFileIsRewrittenOnDisk()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		CodeActionService service = TestServices.CodeActions();
		var getActions = new GetCodeActionsTool(registry, service);
		var subject = new ApplyCodeActionTool(registry, service, new ApplyPipeline());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "unused");

		string actions = await getActions.GetCodeActions(solutionPath, CounterRelativePath, line, column);
		string actionId = ActionIdFor(actions, "CS0219");
		string result = await subject.ApplyCodeAction(solutionPath, actionId);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result.Split('\n')).Contains(entry => entry.TrimStart('\t') == "Counter.razor");
		string counter = await ReadAsync(solutionPath, CounterRelativePath);
		await Assert.That(counter).DoesNotContain("unused");
		await Assert.That(counter).Contains("CurrentCount++;");
	}

	[Test]
	public async Task WhenApplyingAnActionDiscoveredInACshtmlFile_ThenTheCshtmlFileIsRewrittenOnDisk()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		CodeActionService service = TestServices.CodeActions();
		var getActions = new GetCodeActionsTool(registry, service);
		var subject = new ApplyCodeActionTool(registry, service, new ApplyPipeline());
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "unused");

		string actions = await getActions.GetCodeActions(solutionPath, IndexRelativePath, line, column);
		string result = await subject.ApplyCodeAction(solutionPath, ActionIdFor(actions, "CS0219"));

		await Assert.That(result).Contains("applied=Y");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).DoesNotContain("unused");
		await Assert.That(index).Contains("var builder = new StringBuilder();");
	}

	// ---- apply_code_fix ----

	[Test]
	public async Task WhenFixingACompilerDiagnosticInARazorFile_ThenTheRazorFileIsRewrittenOnDisk()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);

		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "unused");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, CounterRelativePath, "CS0219", line, column);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await ReadAsync(solutionPath, CounterRelativePath)).DoesNotContain("unused");
	}

	[Test]
	public async Task WhenFixingACompilerDiagnosticInACshtmlFileWithCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string before = await ReadAsync(solutionPath, IndexRelativePath);
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "unused");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, IndexRelativePath, "CS0219", line, column, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result.Split('\n')).Contains(entry => entry.TrimStart('\t') == "Index.cshtml");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenFixingACompilerDiagnosticInACshtmlFile_ThenTheCshtmlFileIsRewrittenOnDisk()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "unused");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, IndexRelativePath, "CS0219", line, column);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).DoesNotContain("unused");
	}

	[Test]
	public async Task WhenFixingIDE0005InACshtmlFile_ThenTheUnusedUsingLineAtThePositionIsRemoved()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "System.CodeDom");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, IndexRelativePath, "IDE0005", line, column);

		await Assert.That(result).Contains("applied=Y");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).DoesNotContain("@using System.CodeDom");
		await Assert.That(index).Contains("@using System.Text");
	}

	[Test]
	public async Task WhenFixingIDE0005OnAUsedCshtmlUsingLine_ThenItIsNotFoundAndNothingIsWritten()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string before = await ReadAsync(solutionPath, IndexRelativePath);
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "System.Text");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, IndexRelativePath, "IDE0005", line, column);

		await Assert.That(result).Contains("error=NotFound");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenFixingARazorDiagnosticAwayFromItsPosition_ThenItIsNotFound()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		(int line, _) = await PositionOfAsync(solutionPath, CounterRelativePath, "@code");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, CounterRelativePath, "CS0219", line, 1);

		await Assert.That(result).Contains("error=NotFound");
	}

	[Test]
	public async Task WhenFixingADiagnosticAbsentFromTheRazorFile_ThenItIsNotFound()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);

		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "unused");

		string result = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, CounterRelativePath, "CS0168", line, column);

		await Assert.That(result).Contains("error=NotFound");
	}

	// ---- remove_unused_usings ----

	[Test]
	public async Task WhenRemovingUnusedUsingsFromARazorFile_ThenTheUnusedDirectiveIsRemoved()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);

		string result = await TestServices.RemoveUnusedUsings(registry).RemoveUnusedUsings(solutionPath, CounterRelativePath);

		await Assert.That(result).Contains("applied=Y");
		string counter = await ReadAsync(solutionPath, CounterRelativePath);
		await Assert.That(counter).DoesNotContain("@using System.CodeDom");
		await Assert.That(counter).Contains("@code {");
	}

	[Test]
	public async Task WhenRemovingUnusedUsingsFromACshtmlFile_ThenOnlyItsOwnUnusedDirectiveIsRemoved()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string imports = await ReadAsync(solutionPath, "CshtmlLib/Views/_ViewImports.cshtml");

		string result = await TestServices.RemoveUnusedUsings(registry).RemoveUnusedUsings(solutionPath, IndexRelativePath);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("removedCount=1");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).DoesNotContain("@using System.CodeDom");
		await Assert.That(index).StartsWith("@using System.Text\r\n@{");
		await Assert.That(await ReadAsync(solutionPath, "CshtmlLib/Views/_ViewImports.cshtml")).IsEqualTo(imports);
	}

	[Test]
	public async Task WhenRemovingUnusedUsingsAcrossTheSolution_ThenRazorFilesAreIncludedAndImportsAreLeftAlone()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string imports = await ReadAsync(solutionPath, "CshtmlLib/Views/_ViewImports.cshtml");

		string result = await TestServices.RemoveUnusedUsings(registry).RemoveUnusedUsings(solutionPath);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result.Split('\n')).Contains(entry => entry.TrimStart('\t') == "Index.cshtml");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).DoesNotContain("@using System.CodeDom");
		await Assert.That(await ReadAsync(solutionPath, "CshtmlLib/Views/_ViewImports.cshtml")).IsEqualTo(imports);
	}

	[Test]
	public async Task WhenRemovingUnusedUsingsFromARazorFileWithCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		string before = await ReadAsync(solutionPath, CounterRelativePath);

		string result = await TestServices.RemoveUnusedUsings(registry).RemoveUnusedUsings(solutionPath, CounterRelativePath, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result.Split('\n')).Contains(entry => entry.TrimStart('\t') == "Counter.razor");
		await Assert.That(await ReadAsync(solutionPath, CounterRelativePath)).IsEqualTo(before);
	}

	// ---- extract_method ----

	[Test]
	public async Task WhenExtractingFromARazorCodeBlock_ThenTheRazorFileIsRewrittenOnDisk()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "CurrentCount++;");

		string result = await subject.ExtractMethod(solutionPath, CounterRelativePath, line, column, line, column + "CurrentCount++;".Length, methodName: "Bump");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("method=Bump");
		string counter = await ReadAsync(solutionPath, CounterRelativePath);
		// Roslyn formats the new and edited members for the generated class's nesting; they are written back in
		// the file's own tab indentation, and the members it did not touch are left exactly as they were.
		await Assert.That(counter).Contains(NewLines(counter, "\tprotected override void OnInitialized()\n\t{\n\t\tCurrentCount = StartAt;\n\t}\n"));
		await Assert.That(counter).Contains(NewLines(counter, "\tprivate void Bump()\n\t{\n\t\tCurrentCount++;\n\t}\n"));
		await Assert.That(counter).Contains(NewLines(counter, "\tprivate void IncrementCount()\n\t{\n\t\tint unused = 1;\n\t\tBump();\n\t}\n"));
		await Assert.That(counter).DoesNotContain("    ");
	}

	[Test]
	public async Task WhenExtractingALocalFunctionFromACshtmlFunctionsBlock_ThenTheCshtmlFileIsRewrittenOnDisk()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int startColumn, int endLine, int endColumn) = await GreetingSelectionAsync(solutionPath);

		string result = await subject.ExtractMethod(solutionPath, IndexRelativePath, startLine, startColumn, endLine, endColumn, methodName: "AppendGreeting", asLocalFunction: true);

		await Assert.That(result.Contains("applied=Y")).IsTrue();
		await Assert.That(result).Contains("kind=LocalFunction");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).IsEqualTo(ExtractedIndex);
	}

	[Test]
	public async Task WhenASecondEditFollowsAFoldedCshtmlEdit_ThenItStillMapsWithoutAReload()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var extract = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int startColumn, int endLine, int endColumn) = await GreetingSelectionAsync(solutionPath);

		string extracted = await extract.ExtractMethod(solutionPath, IndexRelativePath, startLine, startColumn, endLine, endColumn, methodName: "AppendGreeting", asLocalFunction: true);
		(int line, int column) = await PositionOfAsync(solutionPath, IndexRelativePath, "unused");
		string fixedResult = await TestServices.ApplyCodeFix(registry).ApplyCodeFix(solutionPath, IndexRelativePath, "CS0219", line, column);

		await Assert.That(extracted).Contains("applied=Y");
		await Assert.That(fixedResult.Contains("applied=Y")).IsTrue();
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).IsEqualTo(ExtractedIndex.Replace("\t\tint unused = 1;\r\n", ""));
	}

	/// <summary>Index.cshtml after extracting the two Append statements into a local function, in the file's tab indentation.</summary>
	private const string ExtractedIndex =
		"@using System.Text\r\n" +
		"@using System.CodeDom\r\n" +
		"@{\r\n" +
		"\tvar greeting = Format(\"world\");\r\n" +
		"}\r\n" +
		"<p>@greeting</p>\r\n" +
		"<p>@Format(\"again\")</p>\r\n" +
		"\r\n" +
		"@functions {\r\n" +
		"\tprivate string Format(string name)\r\n" +
		"\t{\r\n" +
		"\t\tint unused = 1;\r\n" +
		"\t\tvar builder = new StringBuilder();\r\n" +
		"\t\tAppendGreeting(name, builder);\r\n" +
		"\t\treturn builder.ToString();\r\n" +
		"\r\n" +
		"\t\tstatic void AppendGreeting(string name, StringBuilder builder)\r\n" +
		"\t\t{\r\n" +
		"\t\t\tbuilder.Append(\"Hello \");\r\n" +
		"\t\t\tbuilder.Append(name);\r\n" +
		"\t\t}\r\n" +
		"\t}\r\n" +
		"}\r\n";

	[Test]
	public async Task WhenRoslynCannotAddAMethodToACshtmlView_ThenTheLocalFunctionAlternativeIsSuggested()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int startLine, int startColumn, int endLine, int endColumn) = await GreetingSelectionAsync(solutionPath);
		string before = await ReadAsync(solutionPath, IndexRelativePath);

		string result = await subject.ExtractMethod(solutionPath, IndexRelativePath, startLine, startColumn, endLine, endColumn);

		await Assert.That(result).Contains("error=NotSupported");
		await Assert.That(result).Contains("asLocalFunction=true");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).IsEqualTo(before);
	}

	/// <summary>The two builder.Append statements in Index.cshtml's Format method, end-exclusive.</summary>
	private static async Task<(int StartLine, int StartColumn, int EndLine, int EndColumn)> GreetingSelectionAsync(string solutionPath)
	{
		(int startLine, int startColumn) = await PositionOfAsync(solutionPath, IndexRelativePath, "builder.Append(\"Hello \");");
		(int endLine, int endColumn) = await PositionOfAsync(solutionPath, IndexRelativePath, "builder.Append(name);");
		return (startLine, startColumn, endLine, endColumn + "builder.Append(name);".Length);
	}

	[Test]
	public async Task WhenExtractingFromARazorFileWithCheckOnly_ThenNothingIsWritten()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "CurrentCount++;");
		string before = await ReadAsync(solutionPath, CounterRelativePath);

		string result = await subject.ExtractMethod(solutionPath, CounterRelativePath, line, column, line, column + "CurrentCount++;".Length, checkOnly: true);

		await Assert.That(result).Contains("applied=N");
		await Assert.That(result.Split('\n')).Contains(entry => entry.TrimStart('\t') == "Counter.razor");
		await Assert.That(await ReadAsync(solutionPath, CounterRelativePath)).IsEqualTo(before);
	}

	[Test]
	public async Task WhenExtractingFromRazorMarkup_ThenItIsNotSupported()
	{
		string solutionPath = await CreateRazorSolutionAsync();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ExtractMethodTool(registry, new ApplyPipeline());
		(int line, int column) = await PositionOfAsync(solutionPath, CounterRelativePath, "<p>Starting");

		string result = await subject.ExtractMethod(solutionPath, CounterRelativePath, line, column, line, column + 5);

		await Assert.That(result).Contains("error=NotSupported");
	}

	// ---- change_signature, rename_symbol, rename_parameter, apply_patch on .cshtml ----

	[Test]
	public async Task WhenChangingTheSignatureOfACshtmlMethod_ThenTheDeclarationAndCallSitesAreRewritten()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ChangeSignatureTool(registry, new SymbolResolver(), new ApplyPipeline());

		string result = await subject.ChangeSignature(solutionPath, $"{IndexTypeName}.Format", "int", "times", "1", callSiteArgument: "2");

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(result).Contains("updatedCallSites=2");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).Contains("private string Format(string name, int times = 1)");
		await Assert.That(index).Contains("Format(\"world\", times: 2)");
		await Assert.That(index).Contains("@Format(\"again\", times: 2)");
	}

	[Test]
	public async Task WhenRenamingACshtmlMethod_ThenTheDeclarationAndCallSitesAreRewritten()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameSymbolTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameSymbol(solutionPath, $"{IndexTypeName}.Format", "Greet");

		await Assert.That(result).Contains("applied=Y");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).Contains("private string Greet(string name)");
		await Assert.That(index).Contains("Greet(\"world\")");
		await Assert.That(index).Contains("@Greet(\"again\")");
		await Assert.That(index).DoesNotContain("Format");
	}

	[Test]
	public async Task WhenRenamingACshtmlMethodParameter_ThenTheCshtmlFileIsRewritten()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new RenameParameterTool(registry, new SymbolResolver(), new ProjectionService(), new ApplyPipeline());

		string result = await subject.RenameParameter(solutionPath, $"{IndexTypeName}.Format", "name", "who");

		await Assert.That(result).Contains("applied=Y");
		string index = await ReadAsync(solutionPath, IndexRelativePath);
		await Assert.That(index).Contains("private string Format(string who)");
		await Assert.That(index).Contains("builder.Append(who);");
	}

	[Test]
	public async Task WhenPatchingACshtmlFile_ThenItIsApplied()
	{
		string solutionPath = TestSolutions.CreateScratchCshtmlSolution();
		using var registry = new InstanceRegistry();
		await registry.GetOrAddAsync(solutionPath);
		var subject = new ApplyPatchTool(registry);
		string patch =
			$"--- a/{IndexRelativePath}\n+++ b/{IndexRelativePath}\n@@ -6,1 +6,1 @@\n-<p>@greeting</p>\n+<p><b>@greeting</b></p>\n";

		string result = await subject.ApplyPatch(solutionPath, patch);

		await Assert.That(result).Contains("applied=Y");
		await Assert.That(await ReadAsync(solutionPath, IndexRelativePath)).Contains("<p><b>@greeting</b></p>");
	}

	// ---- helpers ----

	/// <summary>
	/// A scratch Razor solution whose Counter.razor also has an unused @using and an unused local in its
	/// @code block, so fixes, using removal and extraction have something to act on.
	/// </summary>
	private static async Task<string> CreateRazorSolutionAsync()
	{
		string solutionPath = TestSolutions.CreateScratchRazorSolution();
		string counterPath = FullPath(solutionPath, CounterRelativePath);
		string counter = await File.ReadAllTextAsync(counterPath);
		string newline = counter.Contains("\r\n") ? "\r\n" : "\n";
		counter = counter
			.Replace("@using Microsoft.AspNetCore.Components.Web" + newline, "@using Microsoft.AspNetCore.Components.Web" + newline + "@using System.CodeDom" + newline)
			.Replace("\t\tCurrentCount++;", "\t\tint unused = 1;" + newline + "\t\tCurrentCount++;");
		await File.WriteAllTextAsync(counterPath, counter);
		return solutionPath;
	}

	/// <summary><paramref name="text"/> with its "\n" line breaks converted to the line breaks <paramref name="file"/> uses.</summary>
	private static string NewLines(string file, string text) => file.Contains("\r\n") ? text.Replace("\n", "\r\n") : text;

	private static string FullPath(string solutionPath, string relativePath) =>
		Path.Combine(Path.GetDirectoryName(solutionPath)!, relativePath.Replace('/', Path.DirectorySeparatorChar));

	private static Task<string> ReadAsync(string solutionPath, string relativePath) =>
		File.ReadAllTextAsync(FullPath(solutionPath, relativePath));

	/// <summary>The 1-based line and column of the first occurrence of <paramref name="snippet"/> in the file.</summary>
	private static async Task<(int Line, int Column)> PositionOfAsync(string solutionPath, string relativePath, string snippet)
	{
		string[] lines = (await ReadAsync(solutionPath, relativePath)).Split('\n');
		for (int index = 0; index < lines.Length; index++)
		{
			int column = lines[index].IndexOf(snippet, StringComparison.Ordinal);
			if (column >= 0)
				return (index + 1, column + 1);
		}

		throw new InvalidOperationException($"'{snippet}' was not found in '{relativePath}'.");
	}

	private static string ActionIdFor(string actions, string diagnosticId) =>
		actions.Split('\n')
			.Select(line => line.Trim())
			.First(line => line.Contains($",Fix,{diagnosticId} ", StringComparison.Ordinal))
			.Split(',')[0];
}