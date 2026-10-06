using TUnit.Core.Interfaces;

[assembly: ParallelLimiter<SimCube.Roslynk.CoreTests.WorkspaceTestParallelLimit>]

namespace SimCube.Roslynk.CoreTests;

// Each semantic test can own an MSBuild workspace and compilations. Keep their memory use bounded.
public sealed class WorkspaceTestParallelLimit : IParallelLimit
{
	public int Limit => Math.Min(8, Environment.ProcessorCount);
}
