namespace PeerTalk.Protocols
{
    using Semver;
    using System;
    using System.Linq;

	/// <summary>
	///   A name with a semantic version.
	/// </summary>
	/// <remarks>
	///   Implements value type equality.
	/// </remarks>
	public class VersionedName : IEquatable<VersionedName>, IComparable<VersionedName>
	{
		/// <summary>
		///   The name.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		///   The semantic version.
		/// </summary>
		public SemVersion Version { get; set; }

		/// <inheritdoc />
        public override string ToString() => $"/{Name}/{Version}";

		/// <summary>
        /// Parses the specified string.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <returns>VersionedName.</returns>
        public static VersionedName Parse(string s)
		{
			var parts = s.Split('/').Where(p => p.Length > 0).ToArray();
			return new VersionedName
			{
				Name = string.Join("/", parts, 0, parts.Length - 1),
				Version = SemVersion.Parse(parts[parts.Length - 1], SemVersionStyles.Any)
			};
		}

		/// <inheritdoc />
        public override int GetHashCode() => ToString().GetHashCode();

		/// <inheritdoc />
        public override bool Equals(object obj) =>
            obj is VersionedName that &&
            this.Name == that.Name &&
            this.Version == that.Version;

		/// <inheritdoc />
        public bool Equals(VersionedName that) =>
            this.Name == that.Name &&
            this.Version == that.Version;

		/// <summary>
		///   Value equality.
		/// </summary>
        public static bool operator ==(VersionedName a, VersionedName b) => ReferenceEquals(a, b) || (!(a is null) && !(b is null) && a.Equals(b));

		/// <summary>
		///   Value inequality.
		/// </summary>
        public static bool operator !=(VersionedName a, VersionedName b) => !ReferenceEquals(a, b) && (a is null || b is null || !a.Equals(b));

		/// <inheritdoc />
		public int CompareTo(VersionedName that)
		{
			if (that == null) return 1;
			if (this.Name == that.Name) return this.Version.CompareSortOrderTo(that.Version);
			return this.Name.CompareTo(that.Name);
		}
	}
}
