namespace ICSharpCode.ILSpy.Tests.Analyzers.TestCases.Main
{
	class MainAssembly
	{
		public string UsesSystemStringEmpty()
		{
			return string.Empty;
		}

		public int UsesInt32()
		{
			return int.Parse("1234");
		}
	}
}
