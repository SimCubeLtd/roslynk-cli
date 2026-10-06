using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace SimCube.Roslynk.Protocol;

/// <summary>Per-user local endpoint. Same-user processes are trusted; IPC is never accessible through TCP.</summary>
public sealed class LocalEndpoint
{
	public string DirectoryPath { get; }
	public string SocketPath => Path.Combine(DirectoryPath, "daemon.sock");
	public string PipeName { get; }
	public string LogPath => Path.Combine(DirectoryPath, "daemon.log");

	public LocalEndpoint(string? directoryPath = null)
	{
		string identity = Environment.UserDomainName + ":" + Environment.UserName + ":" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20].ToLowerInvariant();
		DirectoryPath = Path.GetFullPath(directoryPath ?? Environment.GetEnvironmentVariable("ROSLYNK_ENDPOINT_DIRECTORY") ?? Path.Combine(Path.GetTempPath(), "roslynk-" + hash));
		if (directoryPath is null && Environment.GetEnvironmentVariable("ROSLYNK_ENDPOINT_DIRECTORY") is null && !OperatingSystem.IsWindows() && Encoding.UTF8.GetByteCount(SocketPath) > 100)
			DirectoryPath = Path.Combine("/tmp", "roslynk-" + hash);
		PipeName = "roslynk-" + hash + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? DirectoryPath.ToUpperInvariant() : DirectoryPath)))[..12];
		if (!OperatingSystem.IsWindows() && Encoding.UTF8.GetByteCount(SocketPath) > 100)
			throw new IOException("IPC directory path is too long. Set ROSLYNK_ENDPOINT_DIRECTORY to a shorter private path.");
	}

	public void EnsurePrivateDirectory()
	{
		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(DirectoryPath);
			return;
		}
		const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
		Directory.CreateDirectory(DirectoryPath, mode);
		if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0 || File.GetUnixFileMode(DirectoryPath) != mode)
			throw new IOException("Roslynk IPC directory must be a real directory with permissions 0700.");
	}

	/// <summary>The open handle holds an OS sharing lock, released automatically on process death.</summary>
	public FileStream AcquireLock(string name)
	{
		EnsurePrivateDirectory();
		string path = Path.Combine(DirectoryPath, name + ".lock");
		if (new FileInfo(path).LinkTarget is not null)
			throw new IOException("IPC lock must not be a symbolic link.");
		return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	}

	public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
	{
		EnsurePrivateDirectory();
		if (OperatingSystem.IsWindows())
		{
			var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
			try { await pipe.ConnectAsync(cancellationToken); return pipe; }
			catch { await pipe.DisposeAsync(); throw; }
		}
		var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			await socket.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), cancellationToken);
			return new NetworkStream(socket, ownsSocket: true);
		}
		catch { socket.Dispose(); throw; }
	}
}
