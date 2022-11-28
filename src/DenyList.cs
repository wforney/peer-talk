namespace PeerTalk
{
	using System;
	using System.Collections.Concurrent;
	using System.Linq;

	/// <summary>
	/// A sequence of targets that are not approved.
	/// </summary>
	/// <typeparam name="T">
	/// The type of object that the rule applies to.
	/// </typeparam>
	/// <remarks>
	/// Only targets that are not defined will pass.
	/// </remarks>
	public class DenyList<T> : ConcurrentBag<T>, IPolicy<T>
		where T : IEquatable<T>
	{
		/// <inheritdoc />
		public bool IsAllowed(T target) => !this.Contains(target);
	}
}
