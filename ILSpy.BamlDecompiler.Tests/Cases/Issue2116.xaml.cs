namespace ILSpy.BamlDecompiler.Tests.Cases
{
	using System.Windows;
	using System.Windows.Controls;

	public sealed partial class Issue2116 : UserControl
	{
		public static ComponentResourceKey StyleKey1 => new ComponentResourceKey(typeof(Issue2116), "TestStyle1");
		public static ComponentResourceKey StyleKey2 => new ComponentResourceKey(typeof(Issue2116), "TestStyle2");

		public Issue2116()
		{
			InitializeComponent();
		}
	}
}
