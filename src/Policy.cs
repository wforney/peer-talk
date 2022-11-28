namespace PeerTalk
{
	/// <summary>
	///   A base for defining a policy.
	/// </summary>
	/// <typeparam name="T">
	///   The type of object that the rule applies to.
	/// </typeparam>
	public abstract class Policy<T> : IPolicy<T>
	{
		/// <inheritdoc />
		public abstract bool IsAllowed(T target);

		/// <inheritdoc />
		public bool IsNotAllowed(T target) => !IsAllowed(target);
	}
}
