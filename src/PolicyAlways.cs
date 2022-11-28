namespace PeerTalk
{
	/// <summary>
	///   A rule that always passes.
	/// </summary>
	/// <typeparam name="T">
	///   The type of object that the rule applies to.
	/// </typeparam>
	public class PolicyAlways<T> : Policy<T>
	{
		/// <inheritdoc />
		public override bool IsAllowed(T target) => true;
	}
}
