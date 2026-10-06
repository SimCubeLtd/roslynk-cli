using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SimCube.Roslynk.Core.Infrastructure.Lifecycle;
using SimCube.Roslynk.Core.Infrastructure.Results;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbol;
using SimCube.Roslynk.Core.Features.Symbols.GetSymbolBody;
using SimCube.Roslynk.Core.Features.Symbols.GetMembers;
using SimCube.Roslynk.Core.Features.Symbols.FindDefinition;
using SimCube.Roslynk.Core.Features.Symbols.GetExpressionInfo;
using SimCube.Roslynk.Core.Features.Symbols.FindImplementations;
using SimCube.Roslynk.Core.Features.Symbols.GetTypeHierarchy;
using SimCube.Roslynk.Core.Features.Symbols.SearchSymbols;
using SimCube.Roslynk.Core.Features.References.FindReferences;
using SimCube.Roslynk.Core.Features.References.FindReads;
using SimCube.Roslynk.Core.Features.References.FindWrites;
using SimCube.Roslynk.Core.Features.Callers.GetCallers;
using SimCube.Roslynk.Core.Features.DeadCode.FindDeadCode;
using SimCube.Roslynk.Core.Features.Conditionals.FindDeadConditionals;

namespace SimCube.Roslynk.Core.Features.MultiQuery;

/// <summary>
/// Explicit pinned-snapshot operation catalog. Add entries alongside the internal enum and typed query
/// contracts; discovery by reflection must not silently change the supported batch surface.
/// </summary>
/// <remarks>
/// The invoke path is one shared reflective binder (see <see cref="MultiQueryCatalog.InvokeCoreAsync"/>):
/// it resolves the entry's core method once, supplies the pinned <see cref="SolutionModel"/>,
/// <see cref="RoslynInstance"/> and <see cref="CancellationToken"/> parameters itself, binds the caller's
/// arguments by parameter name, honours each parameter's declared C# default when the caller omits it, and
/// rejects unknown argument keys instead of ignoring them. With every user-facing core parameter being
/// string/string?/bool/int, no per-tool binding code exists to drift.
/// </remarks>
internal static class MultiQueryCatalog
{
	/// <summary>Constructs the internal operation from application services.</summary>
	public static object ConstructTool(IServiceProvider provider, OpEntry entry) =>
		ActivatorUtilities.CreateInstance(provider, entry.ToolType);

	internal sealed record OpEntry(
		string ToolName,
		Type ToolType,
		string CoreMethod)
	{
		public MethodInfo? ResolvedMethodField;

		public MethodInfo Resolve() => ResolvedMethodField ??= ToolType.GetMethod(CoreMethod, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			?? throw new InvalidOperationException($"multi_query catalog: '{ToolType.Name}.{CoreMethod}' could not be resolved.");
	}

	/// <summary>
	/// The 14 multi-queryable operations, each pointing at the internal core that takes the pinned
	/// <see cref="SolutionModel"/> instead of acquiring its own. Names must equal the <see cref="MultiQueryOp"/>
	/// members (the resolution test keeps enum == catalog == the tools' name constants in lockstep).
	/// </summary>
	public static readonly FrozenDictionary<string, OpEntry> Entries = new Dictionary<string, OpEntry>(StringComparer.Ordinal) {
		[GetSymbolTool.GetSymbolName] = new(GetSymbolTool.GetSymbolName, typeof(GetSymbolTool), nameof(GetSymbolTool.GetSymbolCoreAsync)),
		[GetSymbolBodyTool.GetSymbolBodyName] = new(GetSymbolBodyTool.GetSymbolBodyName, typeof(GetSymbolBodyTool), nameof(GetSymbolBodyTool.GetSymbolBodyCoreAsync)),
		[GetMembersTool.GetMembersName] = new(GetMembersTool.GetMembersName, typeof(GetMembersTool), nameof(GetMembersTool.GetMembersCoreAsync)),
		[FindDefinitionTool.FindDefinitionName] = new(FindDefinitionTool.FindDefinitionName, typeof(FindDefinitionTool), nameof(FindDefinitionTool.FindDefinitionCoreAsync)),
		[FindImplementationsTool.FindImplementationsName] = new(FindImplementationsTool.FindImplementationsName, typeof(FindImplementationsTool), nameof(FindImplementationsTool.FindImplementationsCoreAsync)),
		[FindReferencesTool.FindReferencesName] = new(FindReferencesTool.FindReferencesName, typeof(FindReferencesTool), nameof(FindReferencesTool.FindReferencesCoreAsync)),
		[GetCallersTool.GetCallersName] = new(GetCallersTool.GetCallersName, typeof(GetCallersTool), nameof(GetCallersTool.GetCallersCoreAsync)),
		[SearchSymbolsTool.SearchSymbolsName] = new(SearchSymbolsTool.SearchSymbolsName, typeof(SearchSymbolsTool), nameof(SearchSymbolsTool.SearchSymbolsCoreAsync)),
		[GetTypeHierarchyTool.GetTypeHierarchyName] = new(GetTypeHierarchyTool.GetTypeHierarchyName, typeof(GetTypeHierarchyTool), nameof(GetTypeHierarchyTool.GetTypeHierarchyCoreAsync)),
		[FindDeadCodeTool.FindDeadCodeName] = new(FindDeadCodeTool.FindDeadCodeName, typeof(FindDeadCodeTool), nameof(FindDeadCodeTool.FindDeadCodeCoreAsync)),
		[FindDeadConditionalsTool.FindDeadConditionalsName] = new(FindDeadConditionalsTool.FindDeadConditionalsName, typeof(FindDeadConditionalsTool), nameof(FindDeadConditionalsTool.FindDeadConditionalsCoreAsync)),
		[GetExpressionInfoTool.GetExpressionInfoName] = new(GetExpressionInfoTool.GetExpressionInfoName, typeof(GetExpressionInfoTool), nameof(GetExpressionInfoTool.GetExpressionInfoCoreAsync)),
		[FindReadsTool.FindReadsName] = new(FindReadsTool.FindReadsName, typeof(FindReadsTool), nameof(FindReadsTool.FindReadsCoreAsync)),
		[FindWritesTool.FindWritesName] = new(FindWritesTool.FindWritesName, typeof(FindWritesTool), nameof(FindWritesTool.FindWritesCoreAsync)),
	}.ToFrozenDictionary(StringComparer.Ordinal);

	/// <summary>
	/// Binds <paramref name="arguments"/> against the entry's core and invokes it on the tool instance built
	/// from <paramref name="provider"/>. The pinned model/instance and the batch's token are supplied here —
	/// never taken from the caller's arguments, which is the line that makes the snapshot guarantee real.
	/// </summary>
	/// <exception cref="ArgumentException">A required argument is missing, an unknown argument key is
	/// present, or a value cannot be coerced to the parameter's type. Mapped to slot error=Invalid.</exception>
	public static async Task<string> InvokeCoreAsync(
		IServiceProvider provider,
		OpEntry entry,
		SolutionModel model,
		RoslynInstance instance,
		IReadOnlyDictionary<string, JsonElement> arguments,
		CancellationToken token)
	{
		object tool = ConstructTool(provider, entry);
		MethodInfo core = entry.Resolve();
		ParameterInfo[] parameters = core.GetParameters();

		// Unknown keys first: the caller's actual mistake is the one to name, before any missing-required
		// error can mask it.
		var bindable = new HashSet<string>(StringComparer.Ordinal);
		foreach (ParameterInfo parameter in parameters)
		{
			if (parameter.ParameterType == typeof(SolutionModel)
				|| parameter.ParameterType == typeof(RoslynInstance)
				|| parameter.ParameterType == typeof(CancellationToken))
				continue;
			bindable.Add(parameter.Name!);
		}
		foreach (string key in arguments.Keys)
		{
			if (!bindable.Contains(key))
				throw new ArgumentException($"'{key}' is not a parameter of '{entry.ToolName}'.");
		}

		var args = new object?[parameters.Length];
		for (int index = 0; index < parameters.Length; index++)
		{
			ParameterInfo parameter = parameters[index];
			if (parameter.ParameterType == typeof(SolutionModel))
			{
				args[index] = model;
				continue;
			}
			if (parameter.ParameterType == typeof(RoslynInstance))
			{
				args[index] = instance;
				continue;
			}
			if (parameter.ParameterType == typeof(CancellationToken))
			{
				args[index] = token;
				continue;
			}

			if (!arguments.TryGetValue(parameter.Name!, out JsonElement value))
			{
				if (!parameter.HasDefaultValue)
					throw new ArgumentException($"Missing required parameter '{parameter.Name}' for '{entry.ToolName}'.");
				args[index] = parameter.DefaultValue;
				continue;
			}

			args[index] = Coerce(parameter.ParameterType, value, entry.ToolName, parameter.Name!);
		}

		// MethodInfo.Invoke wraps any core exception in TargetInvocationException; unwrap it so the per-op
		// boundary maps the core's own exception (and carries its message), not the reflection wrapper.
		object? result;
		try
		{
			result = core.Invoke(tool, args);
		}
		catch (TargetInvocationException invocation)
			when (invocation.InnerException is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(invocation.InnerException).Throw();
			throw; // unreachable
		}
		return await ((Task<string>)result!).ConfigureAwait(false);
	}

	/// <summary>
	/// Converts a JSON argument value to the core parameter's type. Batch requests can represent
	/// scalars, so string forms are accepted for bool/int; anything the target type cannot take is a caller
	/// error (ArgumentException → slot Invalid), never a silent fallback.
	/// </summary>
	private static object? Coerce(Type type, JsonElement value, string toolName, string parameterName)
	{
		string Context() => $"Parameter '{parameterName}' of '{toolName}'";
		try
		{
			if (type == typeof(string) && value.ValueKind == JsonValueKind.Null)
				return null;
			if (type == typeof(string))
				return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

			if (type == typeof(bool))
			{
				if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
					return value.GetBoolean();
				if (value.ValueKind == JsonValueKind.String)
					return bool.Parse(value.GetString()!);
				throw new FormatException($"{Context()} expects true or false.");
			}

			if (type == typeof(int))
			{
				if (value.ValueKind == JsonValueKind.Number)
					return value.GetInt32();
				if (value.ValueKind == JsonValueKind.String)
					return int.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
				throw new FormatException($"{Context()} expects a number.");
			}

			throw new NotSupportedException(
				$"'{toolName}' exposes a parameter type ({type.Name}) the multi_query binder does not support; extend the binder.");
		}
		catch (Exception exception) when (exception is FormatException or JsonException or OverflowException)
		{
			// Re-thrown with the parameter context, since the raw message would not name the offending op.
			throw new FormatException($"{Context()} could not take {value.GetRawText()}: {exception.Message}");
		}
	}
}
