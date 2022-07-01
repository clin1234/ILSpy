using System;
using System.Windows;
using System.Windows.Controls;

namespace ICSharpCode.ILSpy.Themes
{
	internal class ThemeManager
	{
		private bool _isDarkMode;
		private readonly ResourceDictionary _themeDictionaryContainer = new();


		public static readonly ThemeManager Current = new();

		private ThemeManager()
		{
			Application.Current.Resources.MergedDictionaries.Add(_themeDictionaryContainer);
		}

		public bool IsDarkMode {
			get => _isDarkMode;
			set {
				_isDarkMode = value;

				_themeDictionaryContainer.MergedDictionaries.Clear();

				string theme = value ? "Dark" : "Light";

				_themeDictionaryContainer.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"themes/{theme}Theme.xaml", UriKind.Relative) });
			}
		}

		public static Button CreateButton()
		{
			return new Button {
				Style = CreateButtonStyle()
			};
		}

		public static Style CreateButtonStyle()
		{
			return new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
		}

		public static Style CreateToolBarButtonStyle()
		{
			return new Style(typeof(Button), (Style)Application.Current.FindResource(ToolBar.ButtonStyleKey));
		}
	}
}
