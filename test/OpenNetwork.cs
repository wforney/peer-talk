namespace PeerTalk;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   A noop private network.
/// </summary>
internal class OpenNetwork : INetworkProtector
{
	public static int Count;

	public Task<Stream> ProtectAsync(PeerConnection connection, CancellationToken cancel = default)
	{
		Interlocked.Increment(ref Count);
		return Task.FromResult(connection.Stream);
	}
}
