/*
	Copyright (c) 2015 Ki

	Permission is hereby granted, free of charge, to any person obtaining a copy
	of this software and associated documentation files (the "Software"), to deal
	in the Software without restriction, including without limitation the rights
	to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
	copies of the Software, and to permit persons to whom the Software is
	furnished to do so, subject to the following conditions:

	The above copyright notice and this permission notice shall be included in
	all copies or substantial portions of the Software.

	THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
	IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
	FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
	AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
	LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
	OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
	THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace ILSpy.BamlDecompiler.Baml
{
	internal class BamlBinaryReader : BinaryReader
	{
		public BamlBinaryReader(Stream stream)
			: base(stream)
		{
		}

		public int ReadEncodedInt() => Read7BitEncodedInt();
	}

	internal class BamlReader
	{
		const string MSBAML_SIG = "MSBAML";

		internal static bool IsBamlHeader(Stream str)
		{
			var pos = str.Position;
			try
			{
				var rdr = new BinaryReader(str, Encoding.Unicode);
				int len = (int)(rdr.ReadUInt32() >> 1);
				if (len != MSBAML_SIG.Length)
					return false;
				var sig = new string(rdr.ReadChars(len));
				return sig == MSBAML_SIG;
			}
			finally
			{
				str.Position = pos;
			}
		}

		static string ReadSignature(Stream str)
		{
			var rdr = new BinaryReader(str, Encoding.Unicode);
			uint len = rdr.ReadUInt32();
			var sig = new string(rdr.ReadChars((int)(len >> 1)));
			rdr.ReadBytes((int)(((len + 3) & ~3) - len));
			return sig;
		}

		public static BamlDocument ReadDocument(Stream str, CancellationToken token)
		{
			var ret = new BamlDocument();
			var reader = new BamlBinaryReader(str);
			ret.Signature = ReadSignature(str);
			if (ret.Signature != MSBAML_SIG)
				throw new NotSupportedException();
			ret.ReaderVersion = new() { Major = reader.ReadUInt16(), Minor = reader.ReadUInt16() };
			ret.UpdaterVersion = new() { Major = reader.ReadUInt16(), Minor = reader.ReadUInt16() };
			ret.WriterVersion = new() { Major = reader.ReadUInt16(), Minor = reader.ReadUInt16() };
			if (ret.ReaderVersion.Major != 0 || ret.ReaderVersion.Minor != 0x60 ||
				ret.UpdaterVersion.Major != 0 || ret.UpdaterVersion.Minor != 0x60 ||
				ret.WriterVersion.Major != 0 || ret.WriterVersion.Minor != 0x60)
				throw new NotSupportedException();

			var recs = new Dictionary<long, BamlRecord>();
			while (str.Position < str.Length)
			{
				token.ThrowIfCancellationRequested();

				long pos = str.Position;
				var type = (BamlRecordType)reader.ReadByte();
				BamlRecord rec = null;
				rec = type switch {
					BamlRecordType.AssemblyInfo => new AssemblyInfoRecord(),
					BamlRecordType.AttributeInfo => new AttributeInfoRecord(),
					BamlRecordType.ConstructorParametersStart => new ConstructorParametersStartRecord(),
					BamlRecordType.ConstructorParametersEnd => new ConstructorParametersEndRecord(),
					BamlRecordType.ConstructorParameterType => new ConstructorParameterTypeRecord(),
					BamlRecordType.ConnectionId => new ConnectionIdRecord(),
					BamlRecordType.ContentProperty => new ContentPropertyRecord(),
					BamlRecordType.DefAttribute => new DefAttributeRecord(),
					BamlRecordType.DefAttributeKeyString => new DefAttributeKeyStringRecord(),
					BamlRecordType.DefAttributeKeyType => new DefAttributeKeyTypeRecord(),
					BamlRecordType.DeferableContentStart => new DeferableContentStartRecord(),
					BamlRecordType.DocumentEnd => new DocumentEndRecord(),
					BamlRecordType.DocumentStart => new DocumentStartRecord(),
					BamlRecordType.ElementEnd => new ElementEndRecord(),
					BamlRecordType.ElementStart => new ElementStartRecord(),
					BamlRecordType.KeyElementEnd => new KeyElementEndRecord(),
					BamlRecordType.KeyElementStart => new KeyElementStartRecord(),
					BamlRecordType.LineNumberAndPosition => new LineNumberAndPositionRecord(),
					BamlRecordType.LinePosition => new LinePositionRecord(),
					BamlRecordType.LiteralContent => new LiteralContentRecord(),
					BamlRecordType.NamedElementStart => new NamedElementStartRecord(),
					BamlRecordType.OptimizedStaticResource => new OptimizedStaticResourceRecord(),
					BamlRecordType.PIMapping => new PIMappingRecord(),
					BamlRecordType.PresentationOptionsAttribute => new PresentationOptionsAttributeRecord(),
					BamlRecordType.Property => new PropertyRecord(),
					BamlRecordType.PropertyArrayEnd => new PropertyArrayEndRecord(),
					BamlRecordType.PropertyArrayStart => new PropertyArrayStartRecord(),
					BamlRecordType.PropertyComplexEnd => new PropertyComplexEndRecord(),
					BamlRecordType.PropertyComplexStart => new PropertyComplexStartRecord(),
					BamlRecordType.PropertyCustom => new PropertyCustomRecord(),
					BamlRecordType.PropertyDictionaryEnd => new PropertyDictionaryEndRecord(),
					BamlRecordType.PropertyDictionaryStart => new PropertyDictionaryStartRecord(),
					BamlRecordType.PropertyListEnd => new PropertyListEndRecord(),
					BamlRecordType.PropertyListStart => new PropertyListStartRecord(),
					BamlRecordType.PropertyStringReference => new PropertyStringReferenceRecord(),
					BamlRecordType.PropertyTypeReference => new PropertyTypeReferenceRecord(),
					BamlRecordType.PropertyWithConverter => new PropertyWithConverterRecord(),
					BamlRecordType.PropertyWithExtension => new PropertyWithExtensionRecord(),
					BamlRecordType.PropertyWithStaticResourceId => new PropertyWithStaticResourceIdRecord(),
					BamlRecordType.RoutedEvent => new RoutedEventRecord(),
					BamlRecordType.StaticResourceEnd => new StaticResourceEndRecord(),
					BamlRecordType.StaticResourceId => new StaticResourceIdRecord(),
					BamlRecordType.StaticResourceStart => new StaticResourceStartRecord(),
					BamlRecordType.StringInfo => new StringInfoRecord(),
					BamlRecordType.Text => new TextRecord(),
					BamlRecordType.TextWithConverter => new TextWithConverterRecord(),
					BamlRecordType.TextWithId => new TextWithIdRecord(),
					BamlRecordType.TypeInfo => new TypeInfoRecord(),
					BamlRecordType.TypeSerializerInfo => new TypeSerializerInfoRecord(),
					BamlRecordType.XmlnsProperty => new XmlnsPropertyRecord(),
					_ => throw new NotSupportedException(),
				};
				rec.Position = pos;

				rec.Read(reader);
				ret.Add(rec);
				recs.Add(pos, rec);
			}
			for (int i = 0; i < ret.Count; i++)
			{
				if (ret[i] is IBamlDeferRecord defer)
					defer.ReadDefer(ret, i, _ => recs[_]);
			}

			return ret;
		}
	}
}