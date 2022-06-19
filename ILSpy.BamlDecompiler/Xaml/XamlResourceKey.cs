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

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ILSpy.BamlDecompiler.Baml;

namespace ILSpy.BamlDecompiler.Xaml
{
	internal sealed class XamlResourceKey
	{
		XamlResourceKey(BamlNode node)
		{
			KeyNode = node;
			StaticResources = new List<BamlNode>();

			IBamlDeferRecord keyRecord;
			if (node is BamlBlockNode blockNode)
				keyRecord = (IBamlDeferRecord)blockNode.Header;
			else
				keyRecord = (IBamlDeferRecord)((BamlRecordNode)node).Record;

			if (keyRecord.Record.Type == BamlRecordType.ElementEnd)
			{
				Debug.Assert(node.Parent.Footer == keyRecord.Record);
				node.Parent.Annotation = this;
				node.Annotation = this;
				return;
			}

			if (keyRecord.Record.Type != BamlRecordType.ElementStart && node.Parent.Type == BamlRecordType.ElementStart)
			{
				node.Parent.Annotation = this;
				node.Annotation = this;
				return;
			}

			if (keyRecord.Record.Type != BamlRecordType.ElementStart)
			{
				Debug.WriteLine($"Key record @{keyRecord.Position} must be attached to ElementStart (actual {keyRecord.Record.Type})");
			}

			foreach (var child in node.Parent.Children.Where(child => child.Record == keyRecord.Record))
			{
				child.Annotation = this;
				node.Annotation = this;
				return;
			}
			Debug.WriteLine("Cannot find corresponding value element of key record @" + keyRecord.Position);
		}

		public static XamlResourceKey Create(BamlNode node) => new XamlResourceKey(node);

		public BamlNode KeyNode { get; }
		public IList<BamlNode> StaticResources { get; }

		public static XamlResourceKey? FindKeyInSiblings(BamlNode node)
		{
			var children = node.Parent.Children;
			var index = children.IndexOf(node);
			for (int i = index; i >= 0; i--)
			{
				if (children[i].Annotation is XamlResourceKey)
					return children[i].Annotation as XamlResourceKey;
			}
			return null;
		}

		public static XamlResourceKey? FindKeyInAncestors(BamlNode node, out BamlNode? found)
		{
			BamlNode n = node;
			do
			{
				if (n.Annotation is XamlResourceKey key)
				{
					found = n;
					return key;
				}
				n = n.Parent;
			} while (n != null);
			found = null;
			return null;
		}
	}
}