namespace PeerTalk
{
	using Ipfs;
	using System.Collections;
	using System.Collections.Concurrent;
	using System.Collections.Generic;
	using System.Linq;

	/// <summary>
	/// A sequence of filters that are approved.
	/// </summary>
	/// <remarks>
	/// Only targets that are a subset of any filters will pass. If no filters are defined, then
	/// anything passes.
	/// </remarks>
	public class MultiAddressAllowList : ICollection<MultiAddress>, IPolicy<MultiAddress>
	{
		private readonly ConcurrentDictionary<MultiAddress, MultiAddress> filters = new ConcurrentDictionary<MultiAddress, MultiAddress>();

		/// <inheritdoc />
		public int Count => filters.Count;

		/// <inheritdoc />
		public bool IsReadOnly => false;

		/// <inheritdoc />
		public void Add(MultiAddress item) => filters.TryAdd(item, item);

		/// <inheritdoc />
		public void Clear() => filters.Clear();

		/// <inheritdoc />
		public bool Contains(MultiAddress item) => filters.Keys.Contains(item);

		/// <inheritdoc />
		public void CopyTo(MultiAddress[] array, int arrayIndex) => filters.Keys.CopyTo(array, arrayIndex);

		/// <inheritdoc />
		public IEnumerator<MultiAddress> GetEnumerator() => filters.Keys.GetEnumerator();

		/// <inheritdoc />
		IEnumerator IEnumerable.GetEnumerator() => filters.Keys.GetEnumerator();

		/// <inheritdoc />
		public bool IsAllowed(MultiAddress target) => filters.IsEmpty || filters.Any(kvp => Matches(kvp.Key, target));

		/// <inheritdoc />
		public bool Remove(MultiAddress item) => filters.TryRemove(item, out _);

		private bool Matches(MultiAddress filter, MultiAddress target) => filter.Protocols.All(fp => target.Protocols.Any(tp => tp.Code == fp.Code && tp.Value == fp.Value));
	}
}
