using System;
using System.Windows;
using System.Windows.Controls;

using DataGridExtensions;

namespace ICSharpCode.ILSpy.Metadata
{
	/// <summary>
	/// Interaction logic for HexFilterControl.xaml
	/// </summary>
	public sealed partial class HexFilterControl
	{
		TextBox textBox;

		public HexFilterControl()
		{
			InitializeComponent();
		}

		public override void OnApplyTemplate()
		{
			base.OnApplyTemplate();
			textBox = Template.FindName("textBox", this) as TextBox;
		}

		private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			var text = ((TextBox)sender)?.Text;

			Filter = new ContentFilter(text);
		}

		public IContentFilter Filter {
			get { return (IContentFilter)GetValue(FilterProperty); }
			set { SetValue(FilterProperty, value); }
		}
		/// <summary>
		/// Identifies the Filter dependency property
		/// </summary>
		public static readonly DependencyProperty FilterProperty =
			DependencyProperty.Register("Filter", typeof(IContentFilter), typeof(HexFilterControl),
				new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (o, args) => ((HexFilterControl)o).Filter_Changed(args.NewValue)));

		void Filter_Changed(object newValue)
		{
			var textBox = this.textBox;
			if (textBox == null)
				return;

			textBox.Text = (newValue as ContentFilter)?.Value ?? string.Empty;
		}

		sealed class ContentFilter : IContentFilter
		{
			public ContentFilter(string filter)
			{
				this.Value = filter;
			}

			public bool IsMatch(object value)
			{
				if (string.IsNullOrWhiteSpace(Value))
					return true;
				if (value == null)
					return false;

				return $"{value:x8}".IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0;
			}

			public string Value { get; }
		}
	}
}
