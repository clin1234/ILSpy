﻿// Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System.ComponentModel;
using System.Xml.Linq;

using ICSharpCode.ILSpy;
using ICSharpCode.ILSpy.Options;

namespace TestPlugin
{
	[ExportOptionPage(Title = "TestPlugin", Order = 0)]
	partial class CustomOptionPage : IOptionPage
	{
		static readonly XNamespace ns = "http://www.ilspy.net/testplugin";

		public CustomOptionPage()
		{
			InitializeComponent();
		}

		public void Load(ILSpySettings settings)
		{
			// For loading options, use ILSpySetting's indexer.
			// If the specified section does exist, the indexer will return a new empty element.
			XElement e = settings[ns + "CustomOptions"];
			// Now load the options from the XML document:
			Options s = new ();
			s.UselessOption1 = (bool?)e.Attribute("useless1") ?? s.UselessOption1;
			s.UselessOption2 = (double?)e.Attribute("useless2") ?? s.UselessOption2;
			this.DataContext = s;
		}

		public void LoadDefaults()
		{
			this.DataContext = new Options();
		}

		public void Save(XElement root)
		{
			Options s = (Options)this.DataContext;
			// Save the options back into XML:
			XElement section = new (ns + "CustomOptions");
			section.SetAttributeValue("useless1", s.UselessOption1);
			section.SetAttributeValue("useless2", s.UselessOption2);

			// Replace the existing section in the settings file, or add a new section,
			// if required.
			XElement? existingElement = root.Element(ns + "CustomOptions");
			if (existingElement != null)
				existingElement.ReplaceWith(section);
			else
				root.Add(section);
		}
	}

	sealed class Options : INotifyPropertyChanged
	{
		bool uselessOption1;

		public bool UselessOption1 {
			get { return uselessOption1; }
			set {
				if (uselessOption1 != value)
				{
					uselessOption1 = value;
					OnPropertyChanged(nameof(UselessOption1));
				}
			}
		}

		double uselessOption2;

		public double UselessOption2 {
			get { return uselessOption2; }
			set {
				if (uselessOption2 != value)
				{
					uselessOption2 = value;
					OnPropertyChanged(nameof(UselessOption2));
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