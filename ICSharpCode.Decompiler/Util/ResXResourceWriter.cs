﻿// Copyright (c) 2018 Daniel Grunwald
//   This file is based on the Mono implementation of ResXResourceWriter.
//   It is modified to add support for "ResourceSerializedObject" values.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (c) 2004-2005 Novell, Inc.
//
// Authors:
//	Duncan Mak		duncan@ximian.com
//	Gonzalo Paniagua Javier	gonzalo@ximian.com
//	Peter Bartok		pbartok@novell.com
//	Gary Barnett		gary.barnett.mono@gmail.com
//	includes code by Mike Krüger and Lluis Sanchez

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Xml;

namespace ICSharpCode.Decompiler.Util
{
#if INSIDE_SYSTEM_WEB
	internal
#else
	public
#endif
		class ResXResourceWriter : IDisposable
	{
		const string WinFormsAssemblyName =
			", System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

		const string ResXNullRefTypeName = "System.Resources.ResXNullRef" + WinFormsAssemblyName;

		static string schema = @"
	<xsd:schema id='root' xmlns='' xmlns:xsd='http://www.w3.org/2001/XMLSchema' xmlns:msdata='urn:schemas-microsoft-com:xml-msdata'>
		<xsd:element name='root' msdata:IsDataSet='true'>
			<xsd:complexType>
				<xsd:choice maxOccurs='unbounded'>
					<xsd:element name='data'>
						<xsd:complexType>
							<xsd:sequence>
								<xsd:element name='value' type='xsd:string' minOccurs='0' msdata:Ordinal='1' />
								<xsd:element name='comment' type='xsd:string' minOccurs='0' msdata:Ordinal='2' />
							</xsd:sequence>
							<xsd:attribute name='name' type='xsd:string' msdata:Ordinal='1' />
							<xsd:attribute name='type' type='xsd:string' msdata:Ordinal='3' />
							<xsd:attribute name='mimetype' type='xsd:string' msdata:Ordinal='4' />
						</xsd:complexType>
					</xsd:element>
					<xsd:element name='resheader'>
						<xsd:complexType>
							<xsd:sequence>
								<xsd:element name='value' type='xsd:string' minOccurs='0' msdata:Ordinal='1' />
							</xsd:sequence>
							<xsd:attribute name='name' type='xsd:string' use='required' />
						</xsd:complexType>
					</xsd:element>
				</xsd:choice>
			</xsd:complexType>
		</xsd:element>
	</xsd:schema>
".Replace("'", "\"").Replace("\t", "  ");

		#region Public Properties

		public string BasePath { get; set; }

		#endregion

		public virtual void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		void InitWriter()
		{
			if (filename != null)
				stream = File.Open(filename, FileMode.Create);
			if (textwriter == null)
				textwriter = new StreamWriter(stream, Encoding.UTF8);

			writer = new XmlTextWriter(textwriter);
			writer.Formatting = Formatting.Indented;
			writer.WriteStartDocument();
			writer.WriteStartElement("root");
			writer.WriteRaw(schema);
			WriteHeader("resmimetype", "text/microsoft-resx");
			WriteHeader("version", "1.3");
			WriteHeader("reader", "System.Resources.ResXResourceReader" + WinFormsAssemblyName);
			WriteHeader("writer", "System.Resources.ResXResourceWriter" + WinFormsAssemblyName);
		}

		void WriteHeader(string name, string value)
		{
			writer.WriteStartElement("resheader");
			writer.WriteAttributeString("name", name);
			writer.WriteStartElement("value");
			writer.WriteString(value);
			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		void WriteNiceBase64(byte[] value, int offset, int length)
		{
			string b64 = Convert.ToBase64String(value, offset, length);

			// Wild guess; two extra newlines, and one newline/tab pair for every 80 chars
			StringBuilder sb = new(b64, b64.Length + ((b64.Length + 160) / 80) * 3);
			int pos = 0;
			int inc = 80 + Environment.NewLine.Length + 1;
			string ins = Environment.NewLine + "\t";
			while (pos < sb.Length)
			{
				sb.Insert(pos, ins);
				pos += inc;
			}

			sb.Insert(sb.Length, Environment.NewLine);
			writer.WriteString(sb.ToString());
		}

		void WriteBytes(string name, Type type, byte[] value, int offset, int length, string comment)
		{
			writer.WriteStartElement("data");
			writer.WriteAttributeString("name", name);

			if (type != null)
			{
				writer.WriteAttributeString("type", type.AssemblyQualifiedName);
				// byte[] should never get a mimetype, otherwise MS.NET won't be able
				// to parse the data.
				if (type != typeof(byte[]))
					writer.WriteAttributeString("mimetype", ByteArraySerializedObjectMimeType);
				writer.WriteStartElement("value");
				WriteNiceBase64(value, offset, length);
			}
			else
			{
				writer.WriteAttributeString("mimetype", BinSerializedObjectMimeType);
				writer.WriteStartElement("value");
				writer.WriteBase64(value, offset, length);
			}

			writer.WriteEndElement();

			if (!(comment == null || comment.Equals(String.Empty)))
			{
				writer.WriteStartElement("comment");
				writer.WriteString(comment);
				writer.WriteEndElement();
			}

			writer.WriteEndElement();
		}

		void WriteBytes(string name, Type type, byte[] value, string comment)
		{
			WriteBytes(name, type, value, 0, value.Length, comment);
		}

		void WriteString(string name, string value)
		{
			WriteString(name, value, null);
		}

		void WriteString(string name, string value, string type)
		{
			WriteString(name, value, type, String.Empty);
		}

		void WriteString(string name, string value, string type, string comment)
		{
			writer.WriteStartElement("data");
			writer.WriteAttributeString("name", name);
			if (type != null)
				writer.WriteAttributeString("type", type);
			writer.WriteStartElement("value");
			writer.WriteString(value);
			writer.WriteEndElement();
			if (!(comment == null || comment.Equals(String.Empty)))
			{
				writer.WriteStartElement("comment");
				writer.WriteString(comment);
				writer.WriteEndElement();
			}

			writer.WriteEndElement();
			writer.WriteWhitespace("\n  ");
		}

		public void AddResource(string name, byte[] value)
		{
			ArgumentNullException.ThrowIfNull(name);

			ArgumentNullException.ThrowIfNull(value);

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			WriteBytes(name, value.GetType(), value, null);
		}

		public void AddResource(string name, object value)
		{
			AddResource(name, value, String.Empty);
		}

		private void AddResource(string name, object value, string comment)
		{
			if (value is string s)
			{
				AddResource(name, s, comment);
				return;
			}

			ArgumentNullException.ThrowIfNull(name);

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			if (value is byte[] o)
			{
				WriteBytes(name, o.GetType(), o, comment);
				return;
			}

			if (value is ResourceSerializedObject rso)
			{
				var bytes = rso.GetBytes();
				WriteBytes(name, null, bytes, 0, bytes.Length, comment);
				return;
			}

			if (value == null)
			{
				// nulls written as ResXNullRef
				WriteString(name, "", ResXNullRefTypeName, comment);
				return;
			}

			if (value != null && !value.GetType().IsSerializable)
				throw new InvalidOperationException(
					$"The element '{name}' of type '{value.GetType().Name}' is not serializable.");

			TypeConverter converter = TypeDescriptor.GetConverter(value);

			if (converter != null && converter.CanConvertTo(typeof(string)) && converter.CanConvertFrom(typeof(string)))
			{
				string str = converter.ConvertToInvariantString(value);
				WriteString(name, str, value.GetType().AssemblyQualifiedName, comment);
				return;
			}

			if (converter != null && converter.CanConvertTo(typeof(byte[])) && converter.CanConvertFrom(typeof(byte[])))
			{
				byte[] b = (byte[])converter.ConvertTo(value, typeof(byte[]));
				WriteBytes(name, value.GetType(), b, comment);
				return;
			}

			MemoryStream ms = new();
			BinaryFormatter fmt = new();
			try
			{
				fmt.Serialize(ms, value);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException("Cannot add a " + value.GetType() +
				                                    "because it cannot be serialized: " +
				                                    e.Message);
			}

			WriteBytes(name, null, ms.GetBuffer(), 0, (int)ms.Length, comment);
			ms.Close();
		}

		public void AddResource(string name, string value)
		{
			AddResource(name, value, string.Empty);
		}

		private void AddResource(string name, string value, string comment)
		{
			ArgumentNullException.ThrowIfNull(name);

			ArgumentNullException.ThrowIfNull(value);

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			WriteString(name, value, null, comment);
		}

		public void AddMetadata(string name, string value)
		{
			ArgumentNullException.ThrowIfNull(name);

			ArgumentNullException.ThrowIfNull(value);

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			writer.WriteStartElement("metadata");
			writer.WriteAttributeString("name", name);
			writer.WriteAttributeString("xml:space", "preserve");

			writer.WriteElementString("value", value);

			writer.WriteEndElement();
		}

		public void AddMetadata(string name, byte[] value)
		{
			ArgumentNullException.ThrowIfNull(name);

			ArgumentNullException.ThrowIfNull(value);

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			writer.WriteStartElement("metadata");
			writer.WriteAttributeString("name", name);

			writer.WriteAttributeString("type", value.GetType().AssemblyQualifiedName);

			writer.WriteStartElement("value");
			WriteNiceBase64(value, 0, value.Length);
			writer.WriteEndElement();

			writer.WriteEndElement();
		}

		public void AddMetadata(string name, object value)
		{
			if (value is string s)
			{
				AddMetadata(name, s);
				return;
			}

			if (value is byte[] bytes)
			{
				AddMetadata(name, bytes);
				return;
			}

			ArgumentNullException.ThrowIfNull(name);

			ArgumentNullException.ThrowIfNull(value);

			if (!value.GetType().IsSerializable)
				throw new InvalidOperationException(
					$"The element '{name}' of type '{value.GetType().Name}' is not serializable.");

			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			if (writer == null)
				InitWriter();

			Type type = value.GetType();

			TypeConverter converter = TypeDescriptor.GetConverter(value);
			if (converter != null && converter.CanConvertTo(typeof(string)) && converter.CanConvertFrom(typeof(string)))
			{
				string str = converter.ConvertToInvariantString(value);
				writer.WriteStartElement("metadata");
				writer.WriteAttributeString("name", name);
				if (type != null)
					writer.WriteAttributeString("type", type.AssemblyQualifiedName);
				writer.WriteStartElement("value");
				writer.WriteString(str);
				writer.WriteEndElement();
				writer.WriteEndElement();
				writer.WriteWhitespace("\n  ");
				return;
			}

			if (converter != null && converter.CanConvertTo(typeof(byte[])) && converter.CanConvertFrom(typeof(byte[])))
			{
				byte[] b = (byte[])converter.ConvertTo(value, typeof(byte[]));
				writer.WriteStartElement("metadata");
				writer.WriteAttributeString("name", name);

				if (type != null)
				{
					writer.WriteAttributeString("type", type.AssemblyQualifiedName);
					writer.WriteAttributeString("mimetype", ByteArraySerializedObjectMimeType);
					writer.WriteStartElement("value");
					WriteNiceBase64(b, 0, b.Length);
				}
				else
				{
					writer.WriteAttributeString("mimetype", BinSerializedObjectMimeType);
					writer.WriteStartElement("value");
					writer.WriteBase64(b, 0, b.Length);
				}

				writer.WriteEndElement();
				writer.WriteEndElement();
				return;
			}

			MemoryStream ms = new();
			BinaryFormatter fmt = new();
			try
			{
				fmt.Serialize(ms, value);
			}
			catch (Exception e)
			{
				throw new InvalidOperationException("Cannot add a " + value.GetType() +
				                                    "because it cannot be serialized: " +
				                                    e.Message);
			}

			writer.WriteStartElement("metadata");
			writer.WriteAttributeString("name", name);

			if (type != null)
			{
				writer.WriteAttributeString("type", type.AssemblyQualifiedName);
				writer.WriteAttributeString("mimetype", ByteArraySerializedObjectMimeType);
				writer.WriteStartElement("value");
				WriteNiceBase64(ms.GetBuffer(), 0, ms.GetBuffer().Length);
			}
			else
			{
				writer.WriteAttributeString("mimetype", BinSerializedObjectMimeType);
				writer.WriteStartElement("value");
				writer.WriteBase64(ms.GetBuffer(), 0, ms.GetBuffer().Length);
			}

			writer.WriteEndElement();
			writer.WriteEndElement();
			ms.Close();
		}

		public void Close()
		{
			if (writer != null)
			{
				if (!written)
				{
					Generate();
				}

				writer.Close();
				stream = null;
				filename = null;
				textwriter = null;
			}
		}

		public void Generate()
		{
			if (written)
				throw new InvalidOperationException("The resource is already generated.");

			written = true;
			writer.WriteEndElement();
			writer.Flush();
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
				Close();
		}

		#region Local Variables

		private string filename;
		private Stream stream;
		private TextWriter textwriter;
		private XmlTextWriter writer;
		private bool written;

		#endregion // Local Variables

		#region Static Fields

		public static readonly string BinSerializedObjectMimeType = "application/x-microsoft.net.object.binary.base64";

		public static readonly string ByteArraySerializedObjectMimeType =
			"application/x-microsoft.net.object.bytearray.base64";

		public static readonly string DefaultSerializedObjectMimeType = BinSerializedObjectMimeType;
		public static readonly string ResMimeType = "text/microsoft-resx";
		public static readonly string SoapSerializedObjectMimeType = "application/x-microsoft.net.object.soap.base64";
		public static readonly string Version = "2.0";

		#endregion // Static Fields

		#region Constructors & Destructor

		public ResXResourceWriter(Stream stream)
		{
			ArgumentNullException.ThrowIfNull(stream);

			if (!stream.CanWrite)
				throw new ArgumentException("stream is not writable.", nameof(stream));

			this.stream = stream;
		}

		public ResXResourceWriter(TextWriter textWriter)
		{
			this.textwriter = textWriter ?? throw new ArgumentNullException(nameof(textWriter));
		}

		public ResXResourceWriter(string fileName)
		{
			this.filename = fileName ?? throw new ArgumentNullException(nameof(fileName));
		}

		~ResXResourceWriter()
		{
			Dispose(false);
		}

		#endregion // Constructors & Destructor
	}
}