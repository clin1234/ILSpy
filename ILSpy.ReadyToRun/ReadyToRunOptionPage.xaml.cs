// Copyright (c) 2018 Siegfried Pammer
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.ComponentModel;
using System.Xml.Linq;

using ICSharpCode.ILSpy.Options;

namespace ICSharpCode.ILSpy.ReadyToRun
{
	[ExportOptionPage(Title = nameof(global::ILSpy.ReadyToRun.Properties.Resources.ReadyToRun), Order = 40)]
	partial class ReadyToRunOptionPage : IOptionPage
	{
		public ReadyToRunOptionPage()
		{
			InitializeComponent();
		}

		public void Load(ILSpySettings settings)
		{
			Options s = new Options {
				DisassemblyFormat = ReadyToRunOptions.GetDisassemblyFormat(settings),
				IsShowUnwindInfo = ReadyToRunOptions.GetIsShowUnwindInfo(settings),
				IsShowDebugInfo = ReadyToRunOptions.GetIsShowDebugInfo(settings)
			};

			this.DataContext = s;
		}

		public void LoadDefaults()
		{
			this.DataContext = new Options();
		}

		public void Save(XElement root)
		{
			Options s = (Options)this.DataContext;
			ReadyToRunOptions.SetDisassemblyOptions(root, s.DisassemblyFormat, s.IsShowUnwindInfo, s.IsShowDebugInfo);
		}
	}

	internal sealed class Options : INotifyPropertyChanged
	{
		public string[] DisassemblyFormats {
			get {
				return ReadyToRunOptions.disassemblyFormats;
			}
		}

		private readonly bool isShowUnwindInfo;
		public bool IsShowUnwindInfo {
			get {
				return isShowUnwindInfo;
			}
			init {
				isShowUnwindInfo = value;
				OnPropertyChanged(nameof(IsShowUnwindInfo));
			}
		}

		private readonly bool isShowDebugInfo;

		public bool IsShowDebugInfo {
			get {
				return isShowDebugInfo;
			}
			init {
				isShowDebugInfo = value;
				OnPropertyChanged(nameof(IsShowDebugInfo));
			}
		}

		private readonly string? disassemblyFormat;

		public string? DisassemblyFormat {
			get { return disassemblyFormat; }
			init {
				if (disassemblyFormat != value)
				{
					disassemblyFormat = value;
					OnPropertyChanged(nameof(DisassemblyFormat));
				}
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		private void OnPropertyChanged(string propertyName)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}