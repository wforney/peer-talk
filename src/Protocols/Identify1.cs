﻿namespace PeerTalk.Protocols
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using ProtoBuf;
	using Semver;
	using System;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   Identifies the peer.
	/// </summary>
	public class Identify1 : IPeerProtocol
	{
		private readonly ILogger<Identify1> _logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="Identify1"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		public Identify1(ILogger<Identify1> logger) =>
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));

		/// <inheritdoc />
		public string Name { get; } = "ipfs/id";

		/// <inheritdoc />
		public SemVersion Version { get; } = new SemVersion(1, 0);

		/// <inheritdoc />
		public override string ToString() => $"/{Name}/{Version}";

		/// <inheritdoc />
		public async Task ProcessMessageAsync(PeerConnection connection, Stream stream, CancellationToken cancel = default)
		{
			// Send our identity.
			_logger.LogDebug("Sending identity to {ConnectionRemoteAddress}", connection.RemoteAddress);
			var peer = connection.LocalPeer;
			var res = new Identify
			{
				ProtocolVersion = peer.ProtocolVersion,
				AgentVersion = peer.AgentVersion,
				ListenAddresses = peer.Addresses.Select(a => a.WithoutPeerId().ToArray()).ToArray(),
				ObservedAddress = connection.RemoteAddress?.ToArray(),
				Protocols = null, // no longer sent
			};
			if (!(peer.PublicKey is null))
			{
				res.PublicKey = Convert.FromBase64String(peer.PublicKey);
			}

			ProtoBuf.Serializer.SerializeWithLengthPrefix<Identify>(stream, res, PrefixStyle.Base128);
			await stream.FlushAsync().ConfigureAwait(false);
		}

		/// <summary>
		///   Gets the identity information of the remote peer.
		/// </summary>
		/// <param name="connection">
		///   The currenty connection to the remote peer.
		/// </param>
		/// <param name="cancel"></param>
		/// <returns></returns>
		public async Task<Peer> GetRemotePeerAsync(PeerConnection connection, CancellationToken cancel)
		{
			var muxer = await connection.MuxerEstablished.Task.ConfigureAwait(false);
			_logger.LogDebug("Get remote identity");
			Peer remote = connection.RemotePeer;
			if (remote is null)
			{
				remote = new Peer();
				connection.RemotePeer = remote;
			}

			// Read the remote peer identify info.
			using (var stream = await muxer.CreateStreamAsync("id", cancel).ConfigureAwait(false))
			{
				await connection.EstablishProtocolAsync("/multistream/", stream, cancel).ConfigureAwait(false);
				await connection.EstablishProtocolAsync("/ipfs/id/", stream, cancel).ConfigureAwait(false);
				await UpdateRemotePeerAsync(remote, stream, cancel).ConfigureAwait(false);
			}

			// It should always contain the address we used for connections, so
			// that NAT translations are maintained.
			if (!(connection.RemoteAddress is null) && !remote.Addresses.Contains(connection.RemoteAddress))
			{
				var addrs = remote.Addresses.ToList();
				addrs.Add(connection.RemoteAddress);
				remote.Addresses = addrs;
			}

			_ = connection.IdentityEstablished.TrySetResult(remote);

			_logger.LogDebug("Peer id '{Remote}' of {ConnectionRemoteAddress}", remote, connection.RemoteAddress);
			return remote;
		}

		/// <summary>
		///   Read the identify message and update the peer information.
		/// </summary>
		/// <param name="remote"></param>
		/// <param name="stream"></param>
		/// <param name="cancel"></param>
		/// <returns></returns>
		public async Task UpdateRemotePeerAsync(Peer remote, Stream stream, CancellationToken cancel)
		{
			var info = await ProtoBufHelper.ReadMessageAsync<Identify>(stream, cancel).ConfigureAwait(false);

			remote.AgentVersion = info.AgentVersion;
			remote.ProtocolVersion = info.ProtocolVersion;
			if (info.PublicKey is null || info.PublicKey.Length == 0)
			{
				throw new InvalidDataException("Public key is missing.");
			}

			remote.PublicKey = Convert.ToBase64String(info.PublicKey);
			if (remote.Id is null)
			{
				remote.Id = MultiHash.ComputeHash(info.PublicKey);
			}

			if (!(info.ListenAddresses is null))
			{
				remote.Addresses = info.ListenAddresses
					.Select(b => MultiAddress.TryCreate(b))
					.Where(a => !(a is null))
					.Select(a => a.WithPeerId(remote.Id))
					.ToList();
			}

			if (remote.Addresses.Count() == 0)
			{
				_logger.LogWarning("No listen address for {Remote}", remote);
			}

			if (!remote.IsValid())
			{
				throw new InvalidDataException($"Invalid peer {remote}.");
			}
		}

		[ProtoContract]
		private class Identify
		{
			[ProtoMember(5)]
			public string ProtocolVersion;
			[ProtoMember(6)]
			public string AgentVersion;
			[ProtoMember(1)]
			public byte[] PublicKey;
			[ProtoMember(2, IsRequired = true)]
			public byte[][] ListenAddresses;
			[ProtoMember(4)]
			public byte[] ObservedAddress;
			[ProtoMember(3)]
			public string[] Protocols;
		}
	}
}
