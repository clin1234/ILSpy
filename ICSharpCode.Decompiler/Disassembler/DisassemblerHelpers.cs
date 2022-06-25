// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using System.Globalization;
using System.Linq;
using System.Text;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Util;

using SRM = System.Reflection.Metadata;

namespace ICSharpCode.Decompiler.Disassembler
{
	public enum ILNameSyntax
	{
		/// <summary>
		/// class/valuetype + TypeName (built-in types use keyword syntax)
		/// </summary>
		Signature,

		/// <summary>
		/// Like signature, but always refers to type parameters using their position
		/// </summary>
		SignatureNoNamedTypeParameters,

		/// <summary>
		/// [assembly]Full.Type.Name (even for built-in types)
		/// </summary>
		TypeName,

		/// <summary>
		/// Name (but built-in types use keyword syntax)
		/// </summary>
		ShortTypeName
	}

	internal static class DisassemblerHelpers
	{
		static readonly char[] _validNonLetterIdentifierCharacter = { '_', '$', '@', '?', '`', '.' };

		public static string OffsetToString(int offset)
		{
			return $"IL_{offset:x4}";
		}

		public static string OffsetToString(long offset)
		{
			return $"IL_{offset:x4}";
		}

		public static void WriteOffsetReference(ITextOutput writer, int? offset)
		{
			if (offset == null)
				writer.Write("null");
			else
				writer.WriteLocalReference(OffsetToString(offset.Value), offset);
		}

		public static void WriteTo(this SRM.ExceptionRegion exceptionHandler, PEFile? module,
			MetadataGenericContext context,
			ITextOutput writer)
		{
			writer.Write(".try ");
			WriteOffsetReference(writer, exceptionHandler.TryOffset);
			writer.Write('-');
			WriteOffsetReference(writer, exceptionHandler.TryOffset + exceptionHandler.TryLength);
			writer.Write(' ');
			writer.Write(exceptionHandler.Kind.ToString().ToLowerInvariant());
			if (exceptionHandler.FilterOffset != -1)
			{
				writer.Write(' ');
				WriteOffsetReference(writer, exceptionHandler.FilterOffset);
				writer.Write(" handler ");
			}

			if (!exceptionHandler.CatchType.IsNil)
			{
				writer.Write(' ');
				exceptionHandler.CatchType.WriteTo(module, writer, context);
			}

			writer.Write(' ');
			WriteOffsetReference(writer, exceptionHandler.HandlerOffset);
			writer.Write('-');
			WriteOffsetReference(writer, exceptionHandler.HandlerOffset + exceptionHandler.HandlerLength);
		}

		static string ToInvariantCultureString(object value)
		{
			return value is IConvertible convertible
				? convertible.ToString(CultureInfo.InvariantCulture)
				: value.ToString();
		}

		static bool IsValidIdentifierCharacter(char c)
			=> char.IsLetterOrDigit(c) || _validNonLetterIdentifierCharacter.IndexOf(c) >= 0;

		static bool IsValidIdentifier(string identifier)
		{
			if (string.IsNullOrEmpty(identifier))
				return false;

			if (char.IsDigit(identifier[0]))
				return false;

			// As a special case, .ctor and .cctor are valid despite starting with a dot
			if (identifier[0] == '.')
				return identifier is ".ctor" or ".cctor";

			if (identifier.Contains(".."))
				return false;

			if (ILOpCodeExtensions.ILKeywords.Contains(identifier))
				return false;

			return identifier.All(IsValidIdentifierCharacter);
		}

		public static string Escape(string identifier)
		{
			if (IsValidIdentifier(identifier))
			{
				return identifier;
			}

			// The ECMA specification says that ' inside SQString should be ecaped using an octal escape sequence,
			// but we follow Microsoft's ILDasm and use \'.
			return $"'{EscapeString(identifier).Replace("'", "\\'")}'";
		}

		public static void WriteParameterReference(ITextOutput writer, SRM.MetadataReader metadata,
			SRM.MethodDefinitionHandle handle, int sequence)
		{
			var methodDefinition = metadata.GetMethodDefinition(handle);
			var signature = methodDefinition.DecodeSignature(new FullTypeNameSignatureDecoder(metadata), default);
			var parameters = methodDefinition.GetParameters().Select(metadata.GetParameter).ToArray();
			var signatureHeader = signature.Header;
			int index = sequence;
			if (signatureHeader.IsInstance && signature.ParameterTypes.Length == parameters.Length)
			{
				index--;
			}

			if (index < 0 || index >= parameters.Length)
			{
				writer.WriteLocalReference(sequence.ToString(), "param_" + index);
			}
			else
			{
				var param = parameters[index];
				writer.WriteLocalReference(
					param.Name.IsNil ? sequence.ToString() : Escape(metadata.GetString(param.Name)), "param_" + index);
			}
		}

		public static void WriteVariableReference(ITextOutput writer, SRM.MetadataReader metadata,
			SRM.MethodDefinitionHandle handle, int index)
		{
			writer.WriteLocalReference(index.ToString(), "loc_" + index);
		}

		public static void WriteOperand(ITextOutput writer, object operand)
		{
			if (operand is null) throw new ArgumentNullException(nameof(operand));

			if (operand is string s)
			{
				WriteOperand(writer, s);
			}
			else
				switch (operand)
				{
					case char c:
						writer.Write(((int)c).ToString());
						break;
					case float f:
						WriteOperand(writer, f);
						break;
					case double d:
						WriteOperand(writer, d);
						break;
					case bool b:
						writer.Write(b ? "true" : "false");
						break;
					default:
						s = ToInvariantCultureString(operand);
						writer.Write(s);
						break;
				}
		}

		public static void WriteOperand(ITextOutput writer, long val)
		{
			writer.Write(ToInvariantCultureString(val));
		}

		public static void WriteOperand(ITextOutput writer, float val)
		{
			if (val == 0)
			{
				if (float.IsNegativeInfinity(1 / val))
				{
					// negative zero is a special case
					writer.Write('-');
				}

				writer.Write("0.0");
			}
			else if (float.IsInfinity(val) || float.IsNaN(val))
			{
				byte[] data = BitConverter.GetBytes(val);
				writer.Write('(');
				for (int i = 0; i < data.Length; i++)
				{
					if (i > 0)
						writer.Write(' ');
					writer.Write(data[i].ToString("X2"));
				}

				writer.Write(')');
			}
			else
			{
				writer.Write(val.ToString("R", CultureInfo.InvariantCulture));
			}
		}

		public static void WriteOperand(ITextOutput writer, double val)
		{
			if (val == 0)
			{
				if (double.IsNegativeInfinity(1 / val))
				{
					// negative zero is a special case
					writer.Write('-');
				}

				writer.Write("0.0");
			}
			else if (double.IsInfinity(val) || double.IsNaN(val))
			{
				byte[] data = BitConverter.GetBytes(val);
				writer.Write('(');
				for (int i = 0; i < data.Length; i++)
				{
					if (i > 0)
						writer.Write(' ');
					writer.Write(data[i].ToString("X2"));
				}

				writer.Write(')');
			}
			else
			{
				writer.Write(val.ToString("R", CultureInfo.InvariantCulture));
			}
		}

		public static void WriteOperand(ITextOutput writer, string operand)
		{
			writer.Write('"');
			writer.Write(EscapeString(operand));
			writer.Write('"');
		}

		public static string EscapeString(string str)
		{
			var sb = new StringBuilder();
			foreach (char ch in str)
			{
				switch (ch)
				{
					case '"':
						sb.Append("\\\"");
						break;
					case '\\':
						sb.Append("\\\\");
						break;
					case '\0':
						sb.Append("\\0");
						break;
					case '\a':
						sb.Append("\\a");
						break;
					case '\b':
						sb.Append("\\b");
						break;
					case '\f':
						sb.Append("\\f");
						break;
					case '\n':
						sb.Append("\\n");
						break;
					case '\r':
						sb.Append("\\r");
						break;
					case '\t':
						sb.Append("\\t");
						break;
					case '\v':
						sb.Append("\\v");
						break;
					default:
						// print control characters and uncommon white spaces as numbers
						if (char.IsControl(ch) || char.IsSurrogate(ch) || (char.IsWhiteSpace(ch) && ch != ' '))
						{
							sb.Append($"\\u{(int)ch:x4}");
						}
						else
						{
							sb.Append(ch);
						}

						break;
				}
			}

			return sb.ToString();
		}

		public static string? PrimitiveTypeName(string fullName)
		{
			return fullName switch {
				"System.SByte" => "int8",
				"System.Int16" => "int16",
				"System.Int32" => "int32",
				"System.Int64" => "int64",
				"System.Byte" => "uint8",
				"System.UInt16" => "uint16",
				"System.UInt32" => "uint32",
				"System.UInt64" => "uint64",
				"System.Single" => "float32",
				"System.Double" => "float64",
				"System.Void" => "void",
				"System.Boolean" => "bool",
				"System.String" => "string",
				"System.Char" => "char",
				"System.Object" => "object",
				"System.IntPtr" => "native int",
				_ => null
			};
		}
	}
}