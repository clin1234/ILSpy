using System;
using System.Collections.Generic;
using System.IO;

using EnvDTE;

using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace ICSharpCode.ILSpy.AddIn
{
	internal enum MessageButtonResult
	{
		IDOK = 1,
		IDCANCEL = 2,
		IDABORT = 3,
		IDRETRY = 4,
		IDIGNORE = 5,
		IDYES = 6,
		IDNO = 7,
		IDTRYAGAIN = 10,
		IDCONTINUE = 11,
	}

	static class Utils
	{
		public static byte[] HexStringToBytes(string hex)
		{
			if (hex == null) throw new ArgumentNullException(nameof(hex));
			var result = new byte[hex.Length / 2];
			for (int i = 0; i < hex.Length / 2; i++)
			{
				result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
			}
			return result;
		}

		public static bool TryGetProjectFileName(dynamic referenceObject, out string? fileName)
		{
			try
			{
				fileName = referenceObject.Project.FileName;
				return true;
			}
			catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
			{
				fileName = null;
				return false;
			}
		}

		public static object?[] GetProperties(Properties properties, params string[] names)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			var values = new object?[names.Length];
			foreach (object p in properties)
			{
				try
				{
					if (p is Property property)
					{
						for (int i = 0; i < names.Length; i++)
						{
							if (names[i] == property.Name)
							{
								values[i] = property.Value;
								break;
							}
						}
					}
				}
				catch
				{
				}
			}
			return values;
		}

		public static List<(string, object)> GetAllProperties(Properties properties)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			var result = new List<(string, object)>();
			for (int i = 0; i < properties.Count; i++)
			{
				try
				{
					if (properties.Item(i) is { } p)
					{
						result.Add((p.Name, p.Value));
					}
				}
				catch
				{
				}
			}

			return result;
		}

		public static ITextSelection GetSelectionInCurrentView(IServiceProvider serviceProvider, Func<string, bool> predicate)
		{
			IWpfTextViewHost? viewHost = GetCurrentViewHost(serviceProvider, predicate);

			return viewHost?.TextView.Selection;
		}

		public static IWpfTextViewHost? GetCurrentViewHost(IServiceProvider serviceProvider, Func<string, bool> predicate)
		{
			IWpfTextViewHost? viewHost = GetCurrentViewHost(serviceProvider);

			if (viewHost?.GetTextDocument() is not { } textDocument || !predicate(textDocument.FilePath))
				return null;

			return viewHost;
		}

		public static IWpfTextViewHost? GetCurrentViewHost(IServiceProvider serviceProvider)
		{
			if (serviceProvider.GetService(typeof(SVsTextManager)) is not IVsTextManager txtMgr)
			{
				return null;
			}

			const int mustHaveFocus = 1;
			txtMgr.GetActiveView(mustHaveFocus, null, out IVsTextView vTextView);
			if (vTextView is not IVsUserData userData)
				return null;

			Guid guidViewHost = DefGuidList.guidIWpfTextViewHost;
			userData.GetData(ref guidViewHost, out object holder);

			return holder as IWpfTextViewHost;
		}

		public static ITextDocument GetTextDocument(this IWpfTextViewHost viewHost)
		{
			viewHost.TextView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument textDocument);
			return textDocument;
		}

		public static VisualStudioWorkspace? GetWorkspace(IServiceProvider serviceProvider)
		{
			return serviceProvider.GetService(typeof(VisualStudioWorkspace)) as VisualStudioWorkspace;
		}

		public static string? GetProjectOutputAssembly(Project project, Microsoft.CodeAnalysis.Project roslynProject)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			string? outputFileName = Path.GetFileName(roslynProject.OutputFilePath);

			// Get the directory path based on the project file.
			string? projectPath = Path.GetDirectoryName(project.FullName);

			// Get the output path based on the active configuration
			string? projectOutputPath = project.ConfigurationManager.ActiveConfiguration.Properties.Item("OutputPath").Value.ToString();

			// Combine the project path and output path to get the bin path
			if ((projectPath != null) && (projectOutputPath != null) && (outputFileName != null))
				return Path.Combine(projectPath, projectOutputPath, outputFileName);

			return null;
		}
	}
}
