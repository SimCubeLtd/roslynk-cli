using System.Text.Json;

namespace SimCube.Roslynk.Core.Features.MultiQuery;

/// <summary>
/// Internal identifiers for operations that execute against one pinned snapshot. The explicit catalog
/// and typed application/protocol query unions must agree. Historical names are retained for outlines.
/// </summary>
internal enum MultiQueryOp
{
	get_symbol,
	get_symbol_body,
	get_members,
	find_definition,
	find_implementations,
	find_references,
	get_callers,
	search_symbols,
	get_type_hierarchy,
	find_dead_code,
	find_dead_conditionals,
	get_expression_info,
	find_reads,
	find_writes,
}

/// <summary>
/// One operation inside a multi_query batch: which tool to run, plus its arguments. The arguments use
/// exactly that tool's single-call parameter names - anything else is rejected rather than ignored, so a
/// misspelled parameter can never silently fall back to its default. 'solutionId' is deliberately not an
/// argument: the batch carries it once, and every operation runs against the snapshot pinned from it.
/// </summary>
internal sealed record MultiQueryOperation(
	MultiQueryOp Tool,
	IReadOnlyDictionary<string, JsonElement>? Arguments = null);
