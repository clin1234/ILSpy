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
using System.Diagnostics;
using System.Threading;

namespace ILSpy.BamlDecompiler.Baml
{
	internal abstract class BamlNode
	{
		public BamlBlockNode Parent { get; set; }
		public abstract BamlRecordType Type { get; }
		public object Annotation { get; set; }

		public abstract BamlRecord Record { get; }

		public static bool IsHeader(BamlRecord rec)
		{
			return rec.Type switch {
				BamlRecordType.ConstructorParametersStart or BamlRecordType.DocumentStart or BamlRecordType.ElementStart or BamlRecordType.KeyElementStart or BamlRecordType.NamedElementStart or BamlRecordType.PropertyArrayStart or BamlRecordType.PropertyComplexStart or BamlRecordType.PropertyDictionaryStart or BamlRecordType.PropertyListStart or BamlRecordType.StaticResourceStart => true,
				_ => false,
			};
		}

		public static bool IsFooter(BamlRecord rec)
		{
			return rec.Type switch {
				BamlRecordType.ConstructorParametersEnd or BamlRecordType.DocumentEnd or BamlRecordType.ElementEnd or BamlRecordType.KeyElementEnd or BamlRecordType.PropertyArrayEnd or BamlRecordType.PropertyComplexEnd or BamlRecordType.PropertyDictionaryEnd or BamlRecordType.PropertyListEnd or BamlRecordType.StaticResourceEnd => true,
				_ => false,
			};
		}

		public static bool IsMatch(BamlRecord header, BamlRecord footer)
		{
			return header.Type switch {
				BamlRecordType.ConstructorParametersStart => footer.Type == BamlRecordType.ConstructorParametersEnd,
				BamlRecordType.DocumentStart => footer.Type == BamlRecordType.DocumentEnd,
				BamlRecordType.KeyElementStart => footer.Type == BamlRecordType.KeyElementEnd,
				BamlRecordType.PropertyArrayStart => footer.Type == BamlRecordType.PropertyArrayEnd,
				BamlRecordType.PropertyComplexStart => footer.Type == BamlRecordType.PropertyComplexEnd,
				BamlRecordType.PropertyDictionaryStart => footer.Type == BamlRecordType.PropertyDictionaryEnd,
				BamlRecordType.PropertyListStart => footer.Type == BamlRecordType.PropertyListEnd,
				BamlRecordType.StaticResourceStart => footer.Type == BamlRecordType.StaticResourceEnd,
				BamlRecordType.ElementStart or BamlRecordType.NamedElementStart => footer.Type == BamlRecordType.ElementEnd,
				_ => false,
			};
		}

		public static BamlNode Parse(BamlDocument document, CancellationToken token)
		{
			Debug.Assert(document.Count > 0 && document[0].Type == BamlRecordType.DocumentStart);

			BamlBlockNode current = null;
			var stack = new Stack<BamlBlockNode>();

			for (int i = 0; i < document.Count; i++)
			{
				token.ThrowIfCancellationRequested();

				if (IsHeader(document[i]))
				{
					var prev = current;

					current = new() {
						Header = document[i]
					};

					if (prev != null)
					{
						prev.Children.Add(current);
						current.Parent = prev;
						stack.Push(prev);
					}
				}
				else if (IsFooter(document[i]))
				{
					if (current == null)
						throw new Exception("Unexpected footer.");

					while (!IsMatch(current.Header, document[i]))
					{
						// End record can be omited (sometimes).
						if (stack.Count > 0)
							current = stack.Pop();
					}
					current.Footer = document[i];
					if (stack.Count > 0)
						current = stack.Pop();
				}
				else
					current.Children.Add(new BamlRecordNode(document[i]) { Parent = current });
			}
			Debug.Assert(stack.Count == 0);
			return current;
		}
	}

	internal class BamlRecordNode : BamlNode
	{
		BamlRecord record;

		public override BamlRecord Record => record;
		public override BamlRecordType Type => Record.Type;

		public BamlRecordNode(BamlRecord record) => this.record = record;
	}

	internal class BamlBlockNode : BamlNode
	{
		public BamlRecord Header { get; set; }
		public IList<BamlNode> Children { get; }
		public BamlRecord Footer { get; set; }

		public override BamlRecord Record => Header;
		public override BamlRecordType Type => Header.Type;

		public BamlBlockNode() => Children = new List<BamlNode>();
	}
}