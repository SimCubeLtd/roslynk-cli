using SimCube.Roslynk.Cli;
using SimCube.Roslynk.Protocol;
using SimCube.Roslynk.Server;

if (args is ["--daemon"] or ["--daemon", _])
{
	try
	{
		var endpoint = new LocalEndpoint(args.Length == 2 ? args[1] : null);
		if (OperatingSystem.IsWindows())
		{
			endpoint.EnsurePrivateDirectory();
			var log = new StreamWriter(new FileStream(endpoint.LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
			Console.SetOut(log); Console.SetError(log);
		}
		await Daemon.RunAsync(endpoint);
		return 0;
	}
	catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
}
return await new CliApplication().RunAsync(args);
