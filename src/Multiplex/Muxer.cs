namespace PeerTalk.Multiplex
{
	using Ipfs;
	using Microsoft.Extensions.Logging;
	using Nito.AsyncEx;
	using SharedCode.Notifications;
	using System;
	using System.Collections.Concurrent;
	using System.IO;
	using System.Linq;
	using System.Net.Sockets;
	using System.Text;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   Supports multiple protocols over a single channel (stream).
	/// </summary>
	/// <remarks>
	///   See <see href="https://github.com/libp2p/mplex"/> for the spec.
	/// </remarks>
	public class Muxer
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="Muxer"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		/// <param name="notificationService">The notification service.</param>
		/// <exception cref="ArgumentNullException">logger</exception>
		/// <exception cref="ArgumentNullException">notificationService</exception>
		public Muxer(ILogger<Muxer> logger, INotificationService notificationService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
		}

		/// <summary>
		///   The next stream ID to create.
		/// </summary>
		/// <value>
		///   The session initiator allocates even IDs and the session receiver allocates odd IDs.
		/// </value>
		public long NextStreamId { get; private set; } = 1000;

		/// <summary>
		///   The signle channel to exchange protocol messages.
		/// </summary>
		/// <value>
		///   A <see cref="Stream"/> to exchange protocol messages.
		/// </value>
		public Stream Channel { get; set; }

		/// <summary>
		///   The peer connection.
		/// </summary>
		/// <value>
		///   The peer connection that owns this muxer.
		/// </value>
		public PeerConnection Connection { get; set; }

		/// <summary>
		/// The substream created notification.
		/// Implements the <see cref="Notification" />
		/// Sent when the remote end creates a new stream.
		/// </summary>
		/// <seealso cref="Notification" />
		public class SubstreamCreated : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="SubstreamCreated"/> class.
			/// </summary>
			/// <param name="substream">The substream.</param>
			public SubstreamCreated(Substream substream)
			{
				Substream = substream;
			}

			/// <summary>
			/// Gets the substream.
			/// </summary>
			/// <value>The substream.</value>
			public Substream Substream { get; }
		}

		/// <summary>
		/// The substream closed notification.
		/// Implements the <see cref="Notification" />
		/// Sent when the remote end closes a stream.
		/// </summary>
		/// <seealso cref="Notification" />
		public class SubstreamClosed : Notification
		{
			/// <summary>
			/// Initializes a new instance of the <see cref="SubstreamClosed"/> class.
			/// </summary>
			/// <param name="substream">The substream.</param>
			public SubstreamClosed(Substream substream)
			{
				Substream = substream;
			}

			/// <summary>
			/// Gets the substream.
			/// </summary>
			/// <value>The substream.</value>
			public Substream Substream { get; }
		}

		private readonly AsyncLock ChannelWriteLock = new AsyncLock();
		private readonly ILogger<Muxer> _logger;
		private readonly INotificationService _notificationService;

		/// <summary>
		///   The substreams that are open.
		/// </summary>
		/// <value>
		///   The key is stream ID and the value is a <see cref="Substream"/>.
		/// </value>
		public ConcurrentDictionary<long, Substream> Substreams = new ConcurrentDictionary<long, Substream>();

		/// <summary>
		///   Determines if the muxer is the initiator.
		/// </summary>
		/// <value>
		///   <b>true</b> if the muxer is the initiator.
		/// </value>
		/// <seealso cref="Receiver"/>
		public bool Initiator
		{
			get => (NextStreamId & 1) == 0;
			set
			{
				if (value != Initiator)
				{
					NextStreamId += 1;
				}
			}
		}

		/// <summary>
		///   Determines if the muxer is the receiver.
		/// </summary>
		/// <value>
		///   <b>true</b> if the muxer is the receiver.
		/// </value>
		/// <seealso cref="Initiator"/>
		public bool Receiver
		{
			get => !Initiator;
			set => Initiator = !value;
		}

		/// <summary>
		///   Creates a new stream with the specified name.
		/// </summary>
		/// <param name="name">
		///   A name for the stream.
		/// </param>
		/// <param name="cancel">
		///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
		/// </param>
		/// <returns>
		///   A duplex stream.
		/// </returns>
		public async Task<Substream> CreateStreamAsync(string name = "", CancellationToken cancel = default)
		{
			var streamId = NextStreamId;
			NextStreamId += 2;
			var substream = new Substream
			{
				Id = streamId,
				Name = name,
				Muxer = this,
				SentMessageType = PacketType.MessageInitiator,
			};
			_ = Substreams.TryAdd(streamId, substream);

			// Tell the other side about the new stream.
			using (await AcquireWriteAccessAsync().ConfigureAwait(false))
			{
				var header = new Header { StreamId = streamId, PacketType = PacketType.NewStream };
				var wireName = Encoding.UTF8.GetBytes(name);
				await header.WriteAsync(Channel, cancel).ConfigureAwait(false);
				await Channel.WriteVarintAsync(wireName.Length, cancel).ConfigureAwait(false);
				await Channel.WriteAsync(wireName, 0, wireName.Length).ConfigureAwait(false);
				await Channel.FlushAsync().ConfigureAwait(false);
			}

			return substream;
		}

		/// <summary>
		///   Remove the stream.
		/// </summary>
		/// <remarks>
		///   Internal method called by Substream.Dispose().
		/// </remarks>
		public async Task<Substream> RemoveStreamAsync(Substream stream, CancellationToken cancel = default)
		{
			if (Substreams.TryRemove(stream.Id, out Substream _))
			{
				// Tell the other side.
				using (await AcquireWriteAccessAsync().ConfigureAwait(false))
				{
					var header = new Header
					{
						StreamId = stream.Id,
						PacketType = PacketType.CloseInitiator
					};
					await header.WriteAsync(Channel, cancel).ConfigureAwait(false);
					Channel.WriteByte(0); // length
					await Channel.FlushAsync().ConfigureAwait(false);
				}
			}

			return stream;
		}

		/// <summary>
		///   Read the multiplex packets.
		/// </summary>
		/// <param name="cancel"></param>
		/// <returns></returns>
		/// <remarks>
		///   A background task that reads and processes the multiplex packets while
		///   the <see cref="Channel"/> is open and not <paramref name="cancel">cancelled</paramref>.
		///   <para>
		///   Any encountered errors will close the <see cref="Channel"/>.
		///   </para>
		/// </remarks>
		public async Task ProcessRequestsAsync(CancellationToken cancel = default)
		{
			try
			{
				while (Channel.CanRead && !cancel.IsCancellationRequested)
				{
					// Read the packet prefix.
					var header = await Header.ReadAsync(Channel, cancel).ConfigureAwait(false);
					var length = await Varint.ReadVarint32Async(Channel, cancel).ConfigureAwait(false);
					if (_logger.IsEnabled(LogLevel.Trace))
					{
						_logger.LogTrace("received '{PacketType}', stream={StreamId}, length={Length}", header.PacketType, header.StreamId, length);
					}

					// Read the payload.
					var payload = new byte[length];
					await Channel.ReadExactAsync(payload, 0, length, cancel).ConfigureAwait(false);

					// Process the packet
					_ = Substreams.TryGetValue(header.StreamId, out Substream substream);
					switch (header.PacketType)
					{
						case PacketType.NewStream:
							if (!(substream is null))
							{
								_logger.LogWarning("Stream {SubstreamId} already exists", substream.Id);
								continue;
							}

							substream = new Substream
							{
								Id = header.StreamId,
								Name = Encoding.UTF8.GetString(payload),
								Muxer = this
							};
							if (!Substreams.TryAdd(substream.Id, substream))
							{
								// Should not happen.
								throw new Exception($"Stream {substream.Id} already exists");
							}

							_notificationService.Publish(new SubstreamCreated(substream));

							// Special hack for go-ipfs
							if (Receiver && (substream.Id & 1) == 1)
							{
								_logger.LogDebug("go-hack sending newstream {SubstreamId}", substream.Id);
								using (await AcquireWriteAccessAsync().ConfigureAwait(false))
								{
									var hdr = new Header
									{
										StreamId = substream.Id,
										PacketType = PacketType.NewStream
									};
									await hdr.WriteAsync(Channel, cancel).ConfigureAwait(false);
									Channel.WriteByte(0); // length
									await Channel.FlushAsync().ConfigureAwait(false);
								}
							}

							break;

						case PacketType.MessageInitiator:
							if (substream is null)
							{
								_logger.LogWarning("Message to unknown stream #{HeaderStreamId}", header.StreamId);
								continue;
							}

							substream.AddData(payload);
							break;

						case PacketType.MessageReceiver:
							if (substream is null)
							{
								_logger.LogWarning("Message to unknown stream #{HeaderStreamId}", header.StreamId);
								continue;
							}

							substream.AddData(payload);
							break;

						case PacketType.CloseInitiator:
						case PacketType.CloseReceiver:
						case PacketType.ResetInitiator:
						case PacketType.ResetReceiver:
							if (substream is null)
							{
								_logger.LogWarning("Reset of unknown stream #{HeaderStreamId}", header.StreamId);
								continue;
							}

							substream.NoMoreData();
							_ = Substreams.TryRemove(substream.Id, out _);
							_notificationService.Publish(new SubstreamClosed(substream));
							break;

						default:
							throw new InvalidDataException($"Unknown Muxer packet type '{header.PacketType}'.");
					}
				}
			}
			catch (EndOfStreamException)
			{
				// eat it
			}
			catch (IOException)
			{
				// eat it
			}
			catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
			{
				// eat it
			}
			catch (Exception) when (cancel.IsCancellationRequested)
			{
				// eat it
			}
			catch (Exception e)
			{
				// Log error if the channel is not closed.
				if (Channel.CanRead || Channel.CanWrite)
				{
					_logger.LogError(e, "failed");
				}
			}

			// Some of the tests do not pass a connection.
			if (Connection is null)
			{
				Channel?.Dispose();
			}
			else
			{
				Connection.Dispose();
			}

			// Dispose of all the substreams.
			var streams = Substreams.Values.ToArray();
			Substreams.Clear();
			foreach (var stream in streams)
			{
				stream.Dispose();
			}
		}

		/// <summary>
		///   Acquire permission to write to the Channel.
		/// </summary>
		/// <returns>
		///   A task that represents the asynchronous get operation. The task's value
		///   is an <see cref="IDisposable"/> that releases the lock.
		/// </returns>
		public Task<IDisposable> AcquireWriteAccessAsync() => ChannelWriteLock.LockAsync();
	}
}
