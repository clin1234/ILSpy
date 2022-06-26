// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.Documentation
{
	/// <summary>
	/// Provides XML documentation for type and member definitions in source code.
	/// </summary>
	public interface IDocumentationProvider
	{
		/// <summary>
		/// Returns the XML documentation for the given <paramref name="entity"/>.
		/// May return null, if no documentation is present for the entity.
		/// </summary>
		/// <exception cref="ArgumentNullException"><paramref name="entity"/> is null.</exception>
		string GetDocumentation(IEntity entity);
	}

	/// <summary>
	/// Provides documentation from an .xml file (as generated by the Microsoft C# compiler).
	/// </summary>
	/// <remarks>
	/// This class first creates an in-memory index of the .xml file, and then uses that to read only the requested members.
	/// This way, we avoid keeping all the documentation in memory.
	/// The .xml file is only opened when necessary, the file handle is not kept open all the time.
	/// If the .xml file is changed, the index will automatically be recreated.
	/// </remarks>
	[Serializable]
	public class XmlDocumentationProvider : IDeserializationCallback, IDocumentationProvider
	{
		#region Cache
		sealed class XmlDocumentationCache
		{
			readonly KeyValuePair<string, string>[] entries;
			int pos;

			public XmlDocumentationCache(int size = 50)
			{
				if (size <= 0)
					throw new ArgumentOutOfRangeException(nameof(size), size, "Value must be positive");
				this.entries = new KeyValuePair<string, string>[size];
			}

			internal bool TryGet(string key, out string value)
			{
				foreach (var pair in entries)
				{
					if (pair.Key == key)
					{
						value = pair.Value;
						return true;
					}
				}
				value = null;
				return false;
			}

			internal void Add(string key, string value)
			{
				entries[pos++] = new(key, value);
				if (pos == entries.Length)
					pos = 0;
			}
		}
		#endregion

		[Serializable]
		struct IndexEntry : IComparable<IndexEntry>
		{
			/// <summary>
			/// Hash code of the documentation tag
			/// </summary>
			internal readonly int HashCode;

			/// <summary>
			/// Position in the .xml file where the documentation starts
			/// </summary>
			internal readonly int PositionInFile;

			internal IndexEntry(int hashCode, int positionInFile)
			{
				this.HashCode = hashCode;
				this.PositionInFile = positionInFile;
			}

			public int CompareTo(IndexEntry other)
			{
				return this.HashCode.CompareTo(other.HashCode);
			}
		}

		[NonSerialized]
		XmlDocumentationCache cache = new();

		readonly string fileName;
		readonly Encoding encoding;
		volatile IndexEntry[] index; // SORTED array of index entries

		#region Constructor / Redirection support
		/// <summary>
		/// Creates a new XmlDocumentationProvider.
		/// </summary>
		/// <param name="fileName">Name of the .xml file.</param>
		/// <exception cref="IOException">Error reading from XML file (or from redirected file)</exception>
		/// <exception cref="XmlException">Invalid XML file</exception>
		public XmlDocumentationProvider(string fileName)
		{
			if (fileName == null)
				throw new ArgumentNullException(nameof(fileName));

			using FileStream fs = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
			using XmlTextReader xmlReader = new(fs);
			xmlReader.XmlResolver = null; // no DTD resolving
			xmlReader.MoveToContent();
			if (string.IsNullOrEmpty(xmlReader.GetAttribute("redirect")))
			{
				this.fileName = fileName;
				this.encoding = xmlReader.Encoding;
				ReadXmlDoc(xmlReader);
			}
			else
			{
				string redirectionTarget = GetRedirectionTarget(fileName, xmlReader.GetAttribute("redirect"));
				if (redirectionTarget != null)
				{
					Debug.WriteLine("XmlDoc " + fileName + " is redirecting to " + redirectionTarget);
					using FileStream redirectedFs = new(redirectionTarget, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
					using XmlTextReader redirectedXmlReader = new(redirectedFs);
					redirectedXmlReader.XmlResolver = null; // no DTD resolving
					redirectedXmlReader.MoveToContent();
					this.fileName = redirectionTarget;
					this.encoding = redirectedXmlReader.Encoding;
					ReadXmlDoc(redirectedXmlReader);
				}
				else
				{
					throw new XmlException("XmlDoc " + fileName + " is redirecting to " + xmlReader.GetAttribute("redirect") + ", but that file was not found.");
				}
			}
		}

		static string GetRedirectionTarget(string xmlFileName, string target)
		{
			string programFilesDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			programFilesDir = AppendDirectorySeparator(programFilesDir);

			string corSysDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
			corSysDir = AppendDirectorySeparator(corSysDir);

			var fileName = target.Replace("%PROGRAMFILESDIR%", programFilesDir)
				.Replace("%CORSYSDIR%", corSysDir);
			if (!Path.IsPathRooted(fileName))
				fileName = Path.Combine(Path.GetDirectoryName(xmlFileName), fileName);
			return LookupLocalizedXmlDoc(fileName);
		}

		static string AppendDirectorySeparator(string dir)
		{
			if (dir.EndsWith("\\", StringComparison.Ordinal) || dir.EndsWith("/", StringComparison.Ordinal))
				return dir;
			else
				return dir + Path.DirectorySeparatorChar;
		}

		/// <summary>
		/// Given the assembly file name, looks up the XML documentation file name.
		/// Returns null if no XML documentation file is found.
		/// </summary>
		public static string LookupLocalizedXmlDoc(string fileName)
		{
			string xmlFileName = Path.ChangeExtension(fileName, ".xml");
			string currentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
			string localizedXmlDocFile = GetLocalizedName(xmlFileName, currentCulture);

			Debug.WriteLine("Try find XMLDoc @" + localizedXmlDocFile);
			if (File.Exists(localizedXmlDocFile))
			{
				return localizedXmlDocFile;
			}
			Debug.WriteLine("Try find XMLDoc @" + xmlFileName);
			if (File.Exists(xmlFileName))
			{
				return xmlFileName;
			}
			if (currentCulture != "en")
			{
				string englishXmlDocFile = GetLocalizedName(xmlFileName, "en");
				Debug.WriteLine("Try find XMLDoc @" + englishXmlDocFile);
				if (File.Exists(englishXmlDocFile))
				{
					return englishXmlDocFile;
				}
			}
			return null;
		}

		static string GetLocalizedName(string fileName, string language)
		{
			string localizedXmlDocFile = Path.GetDirectoryName(fileName);
			localizedXmlDocFile = Path.Combine(localizedXmlDocFile, language);
			localizedXmlDocFile = Path.Combine(localizedXmlDocFile, Path.GetFileName(fileName));
			return localizedXmlDocFile;
		}
		#endregion

		#region Load / Create Index
		void ReadXmlDoc(XmlTextReader reader)
		{
			//lastWriteDate = File.GetLastWriteTimeUtc(fileName);
			// Open up a second file stream for the line<->position mapping
			using FileStream fs = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
			LinePositionMapper linePosMapper = new(fs, encoding);
			List<IndexEntry> indexList = new();
			while (reader.Read())
			{
				if (reader.IsStartElement())
				{
					switch (reader.LocalName)
					{
						case "members":
							ReadMembersSection(reader, linePosMapper, indexList);
							break;
					}
				}
			}
			indexList.Sort();
			this.index = indexList.ToArray(); // volatile write
		}

		sealed class LinePositionMapper
		{
			readonly FileStream fs;
			readonly Decoder decoder;
			int currentLine = 1;
			char prevChar = '\0';

			// buffers for use with Decoder:
			readonly byte[] input = new byte[1];
			readonly char[] output = new char[2];

			public LinePositionMapper(FileStream fs, Encoding encoding)
			{
				this.decoder = encoding.GetDecoder();
				this.fs = fs;
			}

			public int GetPositionForLine(int line)
			{
				Debug.Assert(line >= currentLine);
				while (line > currentLine)
				{
					int b = fs.ReadByte();
					if (b < 0)
						throw new EndOfStreamException();
					input[0] = (byte)b;
					decoder.Convert(input, 0, 1, output, 0, output.Length, false, out int bytesUsed, out int charsUsed, out _);
					Debug.Assert(bytesUsed == 1);
					if (charsUsed == 1)
					{
						if ((prevChar != '\r' && output[0] == '\n') || output[0] == '\r')
							currentLine++;
						prevChar = output[0];
					}
				}
				return checked((int)fs.Position);
			}
		}

		static void ReadMembersSection(XmlTextReader reader, LinePositionMapper linePosMapper, List<IndexEntry> indexList)
		{
			while (reader.Read())
			{
				switch (reader.NodeType)
				{
					case XmlNodeType.EndElement:
						if (reader.LocalName == "members")
						{
							return;
						}
						break;
					case XmlNodeType.Element:
						if (reader.LocalName == "member")
						{
							int pos = linePosMapper.GetPositionForLine(reader.LineNumber) + Math.Max(reader.LinePosition - 2, 0);
							string memberAttr = reader.GetAttribute("name");
							if (memberAttr != null)
								indexList.Add(new(GetHashCode(memberAttr), pos));
							reader.Skip();
						}
						break;
				}
			}
		}

		/// <summary>
		/// Hash algorithm used for the index.
		/// This is a custom implementation so that old index files work correctly
		/// even when the .NET string.GetHashCode implementation changes
		/// (e.g. due to .NET 4.5 hash randomization)
		/// </summary>
		static int GetHashCode(string key)
		{
			unchecked
			{
				int h = 0;
				foreach (char c in key)
				{
					h = (h << 5) - h + c;
				}
				return h;
			}
		}
		#endregion

		#region GetDocumentation
		/// <summary>
		/// Get the documentation for the member with the specified documentation key.
		/// </summary>
		public string GetDocumentation(string key)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			return GetDocumentation(key, true);
		}

		/// <summary>
		/// Get the documentation for the specified member.
		/// </summary>
		public string GetDocumentation(IEntity entity)
		{
			if (entity == null)
				throw new ArgumentNullException(nameof(entity));
			return GetDocumentation(entity.GetIdString());
		}

		string GetDocumentation(string key, bool allowReload)
		{
			int hashcode = GetHashCode(key);
			var index = this.index; // read volatile field
									// index is sorted, so we can use binary search
			int m = Array.BinarySearch(index, new(hashcode, 0));
			if (m < 0)
				return null;
			// correct hash code found.
			// possibly there are multiple items with the same hash, so go to the first.
			while (--m >= 0 && index[m].HashCode == hashcode)
				;
			// m is now 1 before the first item with the correct hash

			XmlDocumentationCache cache = this.cache;
			lock (cache)
			{
				if (!cache.TryGet(key, out string val))
				{
					try
					{
						// go through all items that have the correct hash
						while (++m < index.Length && index[m].HashCode == hashcode)
						{
							val = LoadDocumentation(key, index[m].PositionInFile);
							if (val != null)
								break;
						}
						// cache the result (even if it is null)
						cache.Add(key, val);
					}
					catch (IOException)
					{
						// may happen if the documentation file was deleted/is inaccessible/changed (EndOfStreamException)
						return allowReload ? ReloadAndGetDocumentation(key) : null;
					}
					catch (XmlException)
					{
						// may happen if the documentation file was changed so that the file position no longer starts on a valid XML element
						return allowReload ? ReloadAndGetDocumentation(key) : null;
					}
				}
				return val;
			}
		}

		string ReloadAndGetDocumentation(string key)
		{
			try
			{
				// Reload the index
				using FileStream fs = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
				using XmlTextReader xmlReader = new(fs);
				xmlReader.XmlResolver = null; // no DTD resolving
				xmlReader.MoveToContent();
				ReadXmlDoc(xmlReader);
			}
			catch (IOException)
			{
				// Ignore errors on reload; IEntity.Documentation callers aren't prepared to handle exceptions
				this.index = Empty<IndexEntry>.Array; // clear index to avoid future load attempts
				return null;
			}
			catch (XmlException)
			{
				this.index = Empty<IndexEntry>.Array; // clear index to avoid future load attempts
				return null;
			}
			return GetDocumentation(key, allowReload: false); // prevent infinite reload loops
		}
		#endregion

		#region Load / Read XML
		string LoadDocumentation(string key, int positionInFile)
		{
			using FileStream fs = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
			fs.Position = positionInFile;
			var context = new XmlParserContext(null, null, null, XmlSpace.None) { Encoding = encoding };
			using XmlTextReader r = new(fs, XmlNodeType.Element, context);
			r.XmlResolver = null; // no DTD resolving
			while (r.Read())
			{
				if (r.NodeType == XmlNodeType.Element)
				{
					string memberAttr = r.GetAttribute("name");
					if (memberAttr == key)
					{
						return r.ReadInnerXml();
					}
					else
					{
						return null;
					}
				}
			}
			return null;
		}
		#endregion

		public virtual void OnDeserialization(object sender)
		{
			cache = new();
		}
	}
}
