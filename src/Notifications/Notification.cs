// <copyright file="Notification.cs" company="improvGroup, LLC">
//     Copyright © 2022 improvGroup, LLC. All Rights Reserved.
// </copyright>

namespace SharedCode.Notifications
{
	/// <summary>
	/// The notification base record.
	/// </summary>
	public abstract class Notification
	{
		/// <summary>
		/// Gets the name.
		/// </summary>
		/// <value>The name.</value>
		protected virtual string Name => this.GetType().Name;

		/// <inheritdoc />
		public override string ToString() => string.IsNullOrWhiteSpace(this.Name) ? "Unknown" : this.Name;
	}
}
