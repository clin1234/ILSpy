#if !OPT
using System;
#endif

namespace ICSharpCode.Decompiler.Tests.TestCases.Pretty
{
	internal class VariableNaming
	{
		private enum MyEnum
		{
			VALUE1 = 1,
			VALUE2
		}

		private class C
		{
			public string Name;
			public string Text;
		}

		private void Test(string text, C c)
		{
#if CS70
			_ = c.Name;
#else
			string name = c.Name;
#endif
		}

		private void Test2(string text, C c)
		{
#if CS70
			_ = c.Text;
#else
			string text2 = c.Text;
#endif
		}

#if !OPT
		private void Issue1841()
		{
			C gen1 = new();
			C gen2 = new();
			C gen3 = new();
			C gen4 = new();
		}

		private void Issue1881()
		{
			object enumLocal4 = new();
#pragma warning restore CS0219
		}
#endif
	}
}
