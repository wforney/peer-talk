namespace JuiceStream
{
	// Part of JuiceStream: https://juicestream.machinezoo.com
	using System;
	using System.IO;
	using System.Threading;
	using System.Threading.Tasks;

	/// <summary>
	/// .NET already has its <c>BufferedStream</c>, but that one will throw unexpected exceptions,
	/// especially on <c>NetworkStreams</c>. JuiceStream's <c>DuplexBufferedStream</c> embeds two
	/// <c>BufferedStream</c> instances, one for each direction, to provide full duplex buffering
	/// over non-seekable streams.
	/// </summary>
	/// <remarks>
	/// Copied from <see
	/// href="https://bitbucket.org/robertvazan/juicestream/raw/2caa975524900d1b5a76ddd3731c273d5dbb51eb/JuiceStream/DuplexBufferedStream.cs" />
	/// </remarks>
	internal class DuplexBufferedStream : Stream
	{
		private readonly Stream Inner;
		private readonly BufferedStream ReadBuffer;
		private readonly BufferedStream WriteBuffer;

		/// <summary>
		/// Initializes a new instance of the <see cref="DuplexBufferedStream" /> class.
		/// </summary>
		/// <param name="stream">The stream.</param>
		public DuplexBufferedStream(Stream stream)
		{
			Inner = stream;
			ReadBuffer = new BufferedStream(stream);
			WriteBuffer = new BufferedStream(stream);
		}

		/// <summary>
		/// When overridden in a derived class, gets a value indicating whether the current stream
		/// supports reading.
		/// </summary>
		/// <value><c>true</c> if this instance can read; otherwise, <c>false</c>.</value>
		public override bool CanRead => Inner.CanRead;

		/// <summary>
		/// When overridden in a derived class, gets a value indicating whether the current stream
		/// supports seeking.
		/// </summary>
		/// <value><c>true</c> if this instance can seek; otherwise, <c>false</c>.</value>
		public override bool CanSeek => false;

		/// <summary>
		/// When overridden in a derived class, gets a value indicating whether the current stream
		/// supports writing.
		/// </summary>
		/// <value><c>true</c> if this instance can write; otherwise, <c>false</c>.</value>
		public override bool CanWrite => Inner.CanWrite;

		/// <summary>
		/// When overridden in a derived class, gets the length in bytes of the stream.
		/// </summary>
		/// <value>The length.</value>
		/// <exception cref="System.NotSupportedException"></exception>
		public override long Length => throw new NotSupportedException();

		/// <summary>
		/// When overridden in a derived class, gets or sets the position within the current stream.
		/// </summary>
		/// <value>The position.</value>
		/// <exception cref="System.NotSupportedException"></exception>
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		/// <summary>
		/// When overridden in a derived class, clears all buffers for this stream and causes any
		/// buffered data to be written to the underlying device.
		/// </summary>
		public override void Flush() => WriteBuffer.Flush();

		/// <summary>
		/// Flushes the asynchronous.
		/// </summary>
		/// <param name="token">
		/// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
		/// </param>
		/// <returns>Task.</returns>
		public override Task FlushAsync(CancellationToken token) => WriteBuffer.FlushAsync(token);

		/// <summary>
		/// When overridden in a derived class, reads a sequence of bytes from the current stream
		/// and advances the position within the stream by the number of bytes read.
		/// </summary>
		/// <param name="buffer">
		/// An array of bytes. When this method returns, the buffer contains the specified byte
		/// array with the values between offset and (offset + count - 1) replaced by the bytes read
		/// from the current source.
		/// </param>
		/// <param name="offset">
		/// The zero-based byte offset in buffer at which to begin storing the data read from the
		/// current stream.
		/// </param>
		/// <param name="count">The maximum number of bytes to be read from the current stream.</param>
		/// <returns>
		/// The total number of bytes read into the buffer. This can be less than the number of
		/// bytes requested if that many bytes are not currently available, or zero (0) if the end
		/// of the stream has been reached.
		/// </returns>
		public override int Read(byte[] buffer, int offset, int count) => ReadBuffer.Read(buffer, offset, count);

		/// <summary>
		/// Reads the asynchronous.
		/// </summary>
		/// <param name="buffer">The buffer.</param>
		/// <param name="offset">The offset.</param>
		/// <param name="count">The count.</param>
		/// <param name="token">
		/// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
		/// </param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
			ReadBuffer.ReadAsync(buffer, offset, count, token);

		/// <summary>
		/// Reads a byte from the stream and advances the position within the stream by one byte, or
		/// returns -1 if at the end of the stream.
		/// </summary>
		/// <returns>The unsigned byte cast to an Int32, or -1 if at the end of the stream.</returns>
		public override int ReadByte() => ReadBuffer.ReadByte();

		/// <summary>
		/// When overridden in a derived class, sets the position within the current stream.
		/// </summary>
		/// <param name="offset">A byte offset relative to the origin parameter.</param>
		/// <param name="origin">
		/// A value of type <see cref="T:System.IO.SeekOrigin"></see> indicating the reference point
		/// used to obtain the new position.
		/// </param>
		/// <returns>The new position within the current stream.</returns>
		/// <exception cref="System.NotSupportedException"></exception>
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		/// <summary>
		/// When overridden in a derived class, sets the length of the current stream.
		/// </summary>
		/// <param name="value">The desired length of the current stream in bytes.</param>
		/// <exception cref="System.NotSupportedException"></exception>
		public override void SetLength(long value) => throw new NotSupportedException();

		/// <summary>
		/// When overridden in a derived class, writes a sequence of bytes to the current stream and
		/// advances the current position within this stream by the number of bytes written.
		/// </summary>
		/// <param name="buffer">
		/// An array of bytes. This method copies count bytes from buffer to the current stream.
		/// </param>
		/// <param name="offset">
		/// The zero-based byte offset in buffer at which to begin copying bytes to the current stream.
		/// </param>
		/// <param name="count">The number of bytes to be written to the current stream.</param>
		public override void Write(byte[] buffer, int offset, int count) =>
			WriteBuffer.Write(buffer, offset, count);

		/// <summary>
		/// Writes the asynchronous.
		/// </summary>
		/// <param name="buffer">The buffer.</param>
		/// <param name="offset">The offset.</param>
		/// <param name="count">The count.</param>
		/// <param name="token">
		/// The cancellation token that can be used by other objects or threads to receive notice of cancellation.
		/// </param>
		/// <returns>Task.</returns>
		public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
			WriteBuffer.WriteAsync(buffer, offset, count, token);

		/// <summary>
		/// Writes a byte to the current position in the stream and advances the position within the
		/// stream by one byte.
		/// </summary>
		/// <param name="value">The byte to write to the stream.</param>
		public override void WriteByte(byte value) =>
			WriteBuffer.WriteByte(value);

		/// <summary>
		/// Releases the unmanaged resources used by the <see cref="T:System.IO.Stream"></see> and
		/// optionally releases the managed resources.
		/// </summary>
		/// <param name="disposing">
		/// true to release both managed and unmanaged resources; false to release only unmanaged resources.
		/// </param>
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				WriteBuffer.Flush();
				Inner.Dispose();
				ReadBuffer.Dispose();
				WriteBuffer.Dispose();
			}
		}
	}
}
