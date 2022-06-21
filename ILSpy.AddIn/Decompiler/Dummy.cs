// Dummy types so that we can use compile some ICS.Decompiler classes in the AddIn context
// without depending on SRM etc.

using System;
using System.Collections.Generic;
using System.Text;

namespace ICSharpCode.Decompiler
{
	public abstract class ReferenceLoadInfo
	{
		public void AddMessage(params object[] args) { }
	}

	enum MessageKind { Warning }

	internal static class MetadataExtensions
	{
		public static string ToHexString(this IEnumerable<byte> bytes, int estimatedLength)
		{
			if (bytes is null) throw new ArgumentNullException(nameof(bytes));

			StringBuilder sb = new StringBuilder(estimatedLength * 2);
			foreach (var b in bytes)
				sb.Append($"{b:x2}");
			return sb.ToString();
		}
	}
}
