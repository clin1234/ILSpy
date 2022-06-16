using System.Windows;

namespace ICSharpCode.ILSpy.Themes
{
	public static class ResourceKeys
	{
		public static readonly ResourceKey TextMarkerBackgroundColor = new ComponentResourceKey(typeof(ResourceKeys), "TextMarkerBackgroundColor");
		public static readonly ResourceKey TextMarkerDefinitionBackgroundColor = new ComponentResourceKey(typeof(ResourceKeys), "TextMarkerDefinitionBackgroundColor");
		public static readonly ResourceKey LinkTextForegroundBrush = new ComponentResourceKey(typeof(ResourceKeys), "LinkTextForegroundBrush");
		public static readonly ResourceKey BracketHighlightBackgroundBrush = new ComponentResourceKey(typeof(ResourceKeys), "BracketHighlightBackgroundBrush");
		public static readonly ResourceKey BracketHighlightBorderPen = new ComponentResourceKey(typeof(ResourceKeys), "BracketHighlightBorderPen");
	}
}
