namespace PeerTalk
{
	/// <summary>
	///   A rule that always fails.
	/// </summary>
	/// <typeparam name="T">
	///   The type of object that the rule applies to.
	/// </typeparam>
	public class PolicyNever<T> : Policy<T>
	{
		/// <inheritdoc />
		public override bool IsAllowed(T target) => false;
	}
}
