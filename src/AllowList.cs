namespace PeerTalk
{
	using System;
	using System.Collections.Concurrent;
	using System.Linq;

	/// <summary>
	/// A sequence of targets that are approved.
	/// </summary>
	/// <typeparam name="T">The type of object that the rule applies to.</typeparam>
	/// <remarks>
	/// Only targets that are defined will pass. If no targets are defined, then anything passes.
	/// </remarks>
	public class AllowList<T> : ConcurrentBag<T>, IPolicy<T>
		where T : IEquatable<T>
	{
		/// <inheritdoc />
		public bool IsAllowed(T target) => this.IsEmpty || this.Contains(target);
	}
}
