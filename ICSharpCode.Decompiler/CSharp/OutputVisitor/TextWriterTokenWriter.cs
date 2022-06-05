﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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
using System.IO;
using System.Text;

using ICSharpCode.Decompiler.CSharp.Syntax;

namespace ICSharpCode.Decompiler.CSharp.OutputVisitor
{
	/// <summary>
	/// Writes C# code into a TextWriter.
	/// </summary>
	internal sealed class TextWriterTokenWriter : TokenWriter, ILocatable
	{
		readonly TextWriter textWriter;
		bool isAtStartOfLine = true;
		int line, column;
		bool needsIndent = true;

		public TextWriterTokenWriter(TextWriter textWriter)
		{
			this.textWriter = textWriter ?? throw new ArgumentNullException(nameof(textWriter));
			this.IndentationString = "\t";
			this.line = 1;
			this.column = 1;
		}

		public int Indentation { get; set; }

		public string IndentationString { get; init; }

		public TextLocation Location {
			get { return new TextLocation(line, column + (needsIndent ? Indentation * IndentationString.Length : 0)); }
		}

		public int Length { get; private set; }

		public override void WriteIdentifier(Identifier identifier)
		{
			WriteIndentation();
			if (identifier.IsVerbatim || CSharpOutputVisitor.IsKeyword(identifier.Name, identifier))
			{
				textWriter.Write('@');
				column++;
				Length++;
			}

			string name = EscapeIdentifier(identifier.Name);
			textWriter.Write(name);
			column += name.Length;
			Length += name.Length;
			isAtStartOfLine = false;
		}

		public override void WriteKeyword(Role role, string keyword)
		{
			WriteIndentation();
			column += keyword.Length;
			Length += keyword.Length;
			textWriter.Write(keyword);
			isAtStartOfLine = false;
		}

		public override void WriteToken(Role role, string token)
		{
			WriteIndentation();
			column += token.Length;
			Length += token.Length;
			textWriter.Write(token);
			isAtStartOfLine = false;
		}

		public override void Space()
		{
			WriteIndentation();
			column++;
			Length++;
			textWriter.Write(' ');
		}

		private void WriteIndentation()
		{
			if (needsIndent)
			{
				needsIndent = false;
				for (int i = 0; i < Indentation; i++)
				{
					textWriter.Write(this.IndentationString);
				}

				column += Indentation * IndentationString.Length;
				Length += Indentation * IndentationString.Length;
			}
		}

		public override void NewLine()
		{
			textWriter.WriteLine();
			column = 1;
			line++;
			Length += textWriter.NewLine.Length;
			needsIndent = true;
			isAtStartOfLine = true;
		}

		public override void Indent()
		{
			Indentation++;
		}

		public override void Unindent()
		{
			Indentation--;
		}

		public override void WriteComment(CommentType commentType, string content)
		{
			WriteIndentation();
			switch (commentType)
			{
				case CommentType.SingleLine:
					textWriter.Write("//");
					textWriter.WriteLine(content);
					Length += 2 + content.Length + textWriter.NewLine.Length;
					column = 1;
					line++;
					needsIndent = true;
					isAtStartOfLine = true;
					break;
				case CommentType.MultiLine:
					textWriter.Write("/*");
					textWriter.Write(content);
					textWriter.Write("*/");
					Length += 4 + content.Length;
					column += 2;
					UpdateEndLocation(content, ref line, ref column);
					column += 2;
					isAtStartOfLine = false;
					break;
				case CommentType.Documentation:
					textWriter.Write("///");
					textWriter.WriteLine(content);
					Length += 3 + content.Length + textWriter.NewLine.Length;
					column = 1;
					line++;
					needsIndent = true;
					isAtStartOfLine = true;
					break;
				case CommentType.MultiLineDocumentation:
					textWriter.Write("/**");
					textWriter.Write(content);
					textWriter.Write("*/");
					Length += 5 + content.Length;
					column += 3;
					UpdateEndLocation(content, ref line, ref column);
					column += 2;
					isAtStartOfLine = false;
					break;
				default:
					textWriter.Write(content);
					column += content.Length;
					Length += content.Length;
					break;
			}
		}

		static void UpdateEndLocation(string content, ref int line, ref int column)
		{
			if (string.IsNullOrEmpty(content))
				return;
			for (int i = 0; i < content.Length; i++)
			{
				char ch = content[i];
				switch (ch)
				{
					case '\r':
						if (i + 1 < content.Length && content[i + 1] == '\n')
							i++;
						goto case '\n';
					case '\n':
						line++;
						column = 0;
						break;
				}

				column++;
			}
		}

		public override void WritePreProcessorDirective(PreProcessorDirectiveType type, string argument)
		{
			// pre-processor directive must start on its own line
			if (!isAtStartOfLine)
				NewLine();
			WriteIndentation();
			textWriter.Write('#');
			string directive = type.ToString().ToLowerInvariant();
			textWriter.Write(directive);
			column += 1 + directive.Length;
			Length += 1 + directive.Length;
			if (!string.IsNullOrEmpty(argument))
			{
				textWriter.Write(' ');
				textWriter.Write(argument);
				column += 1 + argument.Length;
				Length += 1 + argument.Length;
			}

			NewLine();
		}

		public static string PrintPrimitiveValue(object value)
		{
			TextWriter writer = new StringWriter();
			TextWriterTokenWriter tokenWriter = new(writer);
			tokenWriter.WritePrimitiveValue(value);
			return writer.ToString();
		}

		public override void WritePrimitiveValue(object value, LiteralFormat format = LiteralFormat.None)
		{
			switch (value)
			{
				case null:
					// usually NullReferenceExpression should be used for this, but we'll handle it anyways
					textWriter.Write("null");
					column += 4;
					Length += 4;
					return;
				case bool value1:
				{
					if (value1)
					{
						textWriter.Write("true");
						column += 4;
						Length += 4;
					}
					else
					{
						textWriter.Write("false");
						column += 5;
						Length += 5;
					}

					return;
				}
				case string:
				{
					string tmp = ConvertString(value.ToString());
					column += tmp.Length + 2;
					Length += tmp.Length + 2;
					textWriter.Write('"');
					textWriter.Write(tmp);
					textWriter.Write('"');
					break;
				}
				case char c:
				{
					string tmp = ConvertCharLiteral(c);
					column += tmp.Length + 2;
					Length += tmp.Length + 2;
					textWriter.Write('\'');
					textWriter.Write(tmp);
					textWriter.Write('\'');
					break;
				}
				case decimal value2:
				{
					string str = value2.ToString(NumberFormatInfo.InvariantInfo) + "m";
					column += str.Length;
					Length += str.Length;
					textWriter.Write(str);
					break;
				}
				case float f1 when float.IsInfinity(f1) || float.IsNaN(f1):
				{
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					textWriter.Write("float");
					column += 5;
					Length += 5;
					WriteToken(Roles.Dot, ".");
					if (float.IsPositiveInfinity(f1))
					{
						textWriter.Write("PositiveInfinity");
						column += "PositiveInfinity".Length;
						Length += "PositiveInfinity".Length;
					}
					else if (float.IsNegativeInfinity(f1))
					{
						textWriter.Write("NegativeInfinity");
						column += "NegativeInfinity".Length;
						Length += "NegativeInfinity".Length;
					}
					else
					{
						textWriter.Write("NaN");
						column += 3;
						Length += 3;
					}

					return;
				}
				case float f1:
				{
					var str = f1.ToString("R", NumberFormatInfo.InvariantInfo) + "f";
					if (f1 == 0 && float.IsNegativeInfinity(1 / f1) && str[0] != '-')
					{
						// negative zero is a special case
						// (again, not a primitive expression, but it's better to handle
						// the special case here than to do it in all code generators)
						str = '-' + str;
					}

					column += str.Length;
					Length += str.Length;
					textWriter.Write(str);
					break;
				}
				case double d when double.IsInfinity(d) || double.IsNaN(d):
				{
					// Strictly speaking, these aren't PrimitiveExpressions;
					// but we still support writing these to make life easier for code generators.
					textWriter.Write("double");
					column += 6;
					Length += 6;
					WriteToken(Roles.Dot, ".");
					if (double.IsPositiveInfinity(d))
					{
						textWriter.Write("PositiveInfinity");
						column += "PositiveInfinity".Length;
						Length += "PositiveInfinity".Length;
					}
					else if (double.IsNegativeInfinity(d))
					{
						textWriter.Write("NegativeInfinity");
						column += "NegativeInfinity".Length;
						Length += "NegativeInfinity".Length;
					}
					else
					{
						textWriter.Write("NaN");
						column += 3;
						Length += 3;
					}

					return;
				}
				case double d:
				{
					string number = d.ToString("R", NumberFormatInfo.InvariantInfo);
					if (d == 0 && double.IsNegativeInfinity(1 / d) && number[0] != '-')
					{
						// negative zero is a special case
						// (again, not a primitive expression, but it's better to handle
						// the special case here than to do it in all code generators)
						number = '-' + number;
					}

					if (number.IndexOf('.') < 0 && number.IndexOf('E') < 0)
					{
						number += ".0";
					}

					textWriter.Write(number);
					column += number.Length;
					Length += number.Length;
					break;
				}
				case IFormattable formattable:
				{
					StringBuilder b = new();
					if (format == LiteralFormat.HexadecimalNumber)
					{
						b.Append("0x");
						b.Append(formattable.ToString("X", NumberFormatInfo.InvariantInfo));
					}
					else
					{
						b.Append(formattable.ToString(null, NumberFormatInfo.InvariantInfo));
					}

					if (formattable is uint or ulong)
					{
						b.Append("u");
					}

					if (formattable is long or ulong)
					{
						b.Append("L");
					}

					textWriter.Write(b.ToString());
					column += b.Length;
					Length += b.Length;
					break;
				}
				default:
				{
					textWriter.Write(value.ToString());
					int length = value.ToString().Length;
					column += length;
					Length += length;
					break;
				}
			}
		}

		public override void WriteInterpolatedText(string text)
		{
			textWriter.Write(ConvertString(text));
		}

		/// <summary>
		/// Gets the escape sequence for the specified character within a char literal.
		/// Does not include the single quotes surrounding the char literal.
		/// </summary>
		public static string ConvertCharLiteral(char ch)
		{
			if (ch == '\'')
			{
				return "\\'";
			}

			return ConvertChar(ch) ?? ch.ToString();
		}

		/// <summary>
		/// Gets the escape sequence for the specified character.
		/// </summary>
		/// <remarks>This method does not convert ' or ".</remarks>
		static string ConvertChar(char ch)
		{
			switch (ch)
			{
				case '\\':
					return "\\\\";
				case '\0':
					return "\\0";
				case '\a':
					return "\\a";
				case '\b':
					return "\\b";
				case '\f':
					return "\\f";
				case '\n':
					return "\\n";
				case '\r':
					return "\\r";
				case '\t':
					return "\\t";
				case '\v':
					return "\\v";
				case ' ':
				case '_':
				case '`':
				case '^':
					// ASCII characters we allow directly in the output even though we don't use
					// other Unicode characters of the same category.
					return null;
				case '\ufffd':
					return "\\u" + ((int)ch).ToString("x4");
				default:
					switch (char.GetUnicodeCategory(ch))
					{
						case UnicodeCategory.NonSpacingMark:
						case UnicodeCategory.SpacingCombiningMark:
						case UnicodeCategory.EnclosingMark:
						case UnicodeCategory.LineSeparator:
						case UnicodeCategory.ParagraphSeparator:
						case UnicodeCategory.Control:
						case UnicodeCategory.Format:
						case UnicodeCategory.Surrogate:
						case UnicodeCategory.PrivateUse:
						case UnicodeCategory.ConnectorPunctuation:
						case UnicodeCategory.ModifierSymbol:
						case UnicodeCategory.OtherNotAssigned:
						case UnicodeCategory.SpaceSeparator:
							return "\\u" + ((int)ch).ToString("x4");
						default:
							return null;
					}
			}
		}

		/// <summary>
		/// Converts special characters to escape sequences within the given string.
		/// </summary>
		public static string ConvertString(string str)
		{
			StringBuilder sb = new();
			foreach (char ch in str)
			{
				string s = ch == '"' ? "\\\"" : ConvertChar(ch);
				if (s != null)
					sb.Append(s);
				else
					sb.Append(ch);
			}

			return sb.ToString();
		}

		public static string EscapeIdentifier(string identifier)
		{
			if (string.IsNullOrEmpty(identifier))
				return identifier;
			StringBuilder sb = new();
			for (int i = 0; i < identifier.Length; i++)
			{
				if (IsPrintableIdentifierChar(identifier, i))
				{
					if (char.IsSurrogatePair(identifier, i))
					{
						sb.Append(identifier.Substring(i, 2));
						i++;
					}
					else
					{
						sb.Append(identifier[i]);
					}
				}
				else
				{
					if (char.IsSurrogatePair(identifier, i))
					{
						sb.Append($"\\U{char.ConvertToUtf32(identifier, i):x8}");
						i++;
					}
					else
					{
						sb.Append($"\\u{(int)identifier[i]:x4}");
					}
				}
			}

			return sb.ToString();
		}

		public static bool ContainsNonPrintableIdentifierChar(string identifier)
		{
			if (string.IsNullOrEmpty(identifier))
				return false;

			for (int i = 0; i < identifier.Length; i++)
			{
				if (char.IsWhiteSpace(identifier[i]))
					return true;
				if (!IsPrintableIdentifierChar(identifier, i))
					return true;
			}

			return false;
		}

		static bool IsPrintableIdentifierChar(string identifier, int index)
		{
			switch (identifier[index])
			{
				case '\\':
					return false;
				case ' ':
				case '_':
				case '`':
				case '^':
					return true;
			}

			switch (char.GetUnicodeCategory(identifier, index))
			{
				case UnicodeCategory.NonSpacingMark:
				case UnicodeCategory.SpacingCombiningMark:
				case UnicodeCategory.EnclosingMark:
				case UnicodeCategory.LineSeparator:
				case UnicodeCategory.ParagraphSeparator:
				case UnicodeCategory.Control:
				case UnicodeCategory.Format:
				case UnicodeCategory.Surrogate:
				case UnicodeCategory.PrivateUse:
				case UnicodeCategory.ConnectorPunctuation:
				case UnicodeCategory.ModifierSymbol:
				case UnicodeCategory.OtherNotAssigned:
				case UnicodeCategory.SpaceSeparator:
					return false;
				default:
					return true;
			}
		}

		public override void WritePrimitiveType(string type)
		{
			textWriter.Write(type);
			column += type.Length;
			Length += type.Length;
			if (type == "new")
			{
				textWriter.Write("()");
				column += 2;
				Length += 2;
			}
		}

		public override void StartNode(AstNode node)
		{
			// Write out the indentation, so that overrides of this method
			// can rely use the current output length to identify the position of the node
			// in the output.
			WriteIndentation();
		}

		public override void EndNode(AstNode node)
		{
		}
	}
}