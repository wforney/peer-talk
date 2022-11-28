namespace PeerTalk
{
	using Ipfs.CoreApi;
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	///   A simple wrapper around another stream that records statistics.
	/// </summary>
	public class StatsStream : Stream
	{
		/// <summary>
		///   A summary of all StatStreams.
		/// </summary>
		public static BandwidthData AllBandwidth = new BandwidthData
		{
			RateIn = 5 * 1024,
			RateOut = 1024
		};

		private readonly Stream stream;

		static StatsStream()
		{
			_ = Task.Run(
				async () =>
				{
					while (true)
					{
						await Task.Delay(1000).ConfigureAwait(false);
						lock (AllBandwidth)
						{
							AllBandwidth.RateIn = 0;
							AllBandwidth.RateOut = 0;
						}
					}
				});
		}

		/// <summary>
		///   Create a <see cref="StatsStream"/> for the specified stream.
		/// </summary>
		public StatsStream(Stream stream)
		{
			this.stream = stream;
		}

		/// <summary>
		///   Total number of bytes read on the stream.
		/// </summary>
		public long BytesRead { get; private set; }

		/// <summary>
		///   Total number of byte written to the stream.
		/// </summary>
		public long BytesWritten { get; private set; }

		/// <summary>
		///   The last time a write or read occured.
		/// </summary>
		public DateTime LastUsed { get; private set; }

		/// <inheritdoc />
		public override bool CanRead => stream.CanRead;

		/// <inheritdoc />
		public override bool CanSeek => stream.CanSeek;

		/// <inheritdoc />
		public override bool CanWrite => stream.CanWrite;

		/// <inheritdoc />
		public override long Length => stream.Length;

		/// <inheritdoc />
		public override bool CanTimeout => stream.CanTimeout;

		/// <inheritdoc />
		public override int ReadTimeout { get => stream.ReadTimeout; set => stream.ReadTimeout = value; }

		/// <inheritdoc />
		public override long Position { get => stream.Position; set => stream.Position = value; }

		/// <inheritdoc />
		public override int WriteTimeout { get => stream.WriteTimeout; set => stream.WriteTimeout = value; }

		/// <inheritdoc />
		public override void Flush() => stream.Flush();

		/// <inheritdoc />
		public override int Read(byte[] buffer, int offset, int count)
		{
			var n = stream.Read(buffer, offset, count);
			BytesRead += n;
			LastUsed = DateTime.Now;
			if (n > 0)
			{
				//lock (AllBandwidth)
				{
					AllBandwidth.TotalIn += (ulong)n;
					AllBandwidth.RateIn += n;
				}
			}

			return n;
		}

		/// <inheritdoc />
		public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

		/// <inheritdoc />
		public override void SetLength(long value) => stream.SetLength(value);

		/// <inheritdoc />
		public override void Write(byte[] buffer, int offset, int count)
		{
			stream.Write(buffer, offset, count);
			BytesWritten += count;
			LastUsed = DateTime.Now;
			if (count > 0)
			{
				//lock (AllBandwidth)
				{
					AllBandwidth.TotalOut += (ulong)count;
					AllBandwidth.RateOut += count;
				}
			}
		}

		/// <inheritdoc />
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				stream.Dispose();
			}

			base.Dispose(disposing);
		}

		/// <inheritdoc />
		public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);

		/// <inheritdoc />
		public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
		{
			try
			{
				var n = await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
				BytesRead += n;
				LastUsed = DateTime.Now;
				if (n > 0)
				{
					//lock (AllBandwidth)
					{
						AllBandwidth.TotalIn += (ulong)n;
						AllBandwidth.RateIn += n;
					}
				}

				return n;
			}
			catch (Exception) when (cancellationToken != default && cancellationToken.IsCancellationRequested)
			{
				// eat it.
				return 0;
			}
		}

		/// <inheritdoc />
		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			try
			{
				await stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
				BytesWritten += count;
				LastUsed = DateTime.Now;
				if (count > 0)
				{
					//lock (AllBandwidth)
					{
						AllBandwidth.TotalOut += (ulong)count;
						AllBandwidth.RateOut += count;
					}
				}
			}
			catch (Exception) when (cancellationToken != null && cancellationToken.IsCancellationRequested)
			{
				// eat it.
			}
		}

		/// <inheritdoc />
		public override int ReadByte()
		{
			var n = stream.ReadByte();
			if (n > -1)
			{
				++BytesRead;
				//lock (AllBandwidth)
				{
					++AllBandwidth.TotalIn;
					++AllBandwidth.RateIn;
				}
			}

			LastUsed = DateTime.Now;
			return n;
		}

		/// <inheritdoc />
		public override void WriteByte(byte value)
		{
			stream.WriteByte(value);
			++BytesWritten;
			LastUsed = DateTime.Now;
			//lock (AllBandwidth)
			{
				++AllBandwidth.TotalOut;
				++AllBandwidth.RateOut;
			}
		}
	}
}
