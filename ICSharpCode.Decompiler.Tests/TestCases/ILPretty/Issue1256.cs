using System;

namespace ICSharpCode.Decompiler.Tests.TestCases.ILPretty
{
	internal class Issue1256
	{
		public void Method(Enum e, object o, string s)
		{
			object obj = new();
			long num3 = (long)o;
		}
	}
}
