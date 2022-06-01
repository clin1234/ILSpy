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
using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;
using ICSharpCode.Decompiler.Semantics;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	sealed class TypePattern : Pattern
	{
		readonly string name;
		readonly string ns;

		public TypePattern(Type type)
		{
			this.ns = type.Namespace;
			this.name = type.Name;
		}

		public override bool DoMatch(INode? other, Match match)
		{
			AstType o;
			if (other is ComposedType { HasRefSpecifier: false, HasNullableSpecifier: false, PointerRank: 0 } ct &&
			    !ct.ArraySpecifiers.Any())
			{
				// Special case: ILSpy sometimes produces a ComposedType but then removed all array specifiers
				// from it. In that case, we need to look at the base type for the annotations.
				o = ct.BaseType;
			}
			else
			{
				o = other as AstType;
				if (o == null)
					return false;
			}

			return o.GetResolveResult() is TypeResolveResult trr && trr.Type.Namespace == ns && trr.Type.Name == name;
		}

		public override string ToString()
		{
			return name;
		}
	}

	sealed class LdTokenPattern : Pattern
	{
		readonly AnyNode childNode;

		public LdTokenPattern(string groupName)
		{
			this.childNode = new AnyNode(groupName);
		}

		public override bool DoMatch(INode? other, Match match)
		{
			if (other is InvocationExpression ie && ie.Annotation<LdTokenAnnotation>() != null &&
			    ie.Arguments.Count == 1)
			{
				return childNode.DoMatch(ie.Arguments.Single(), match);
			}

			return false;
		}

		public override string ToString()
		{
			return "ldtoken(...)";
		}
	}

	/// <summary>
	/// typeof-Pattern that applies on the expanded form of typeof (prior to ReplaceMethodCallsWithOperators)
	/// </summary>
	sealed class TypeOfPattern : Pattern
	{
		readonly INode childNode;

		public TypeOfPattern(string groupName)
		{
			childNode = new MemberReferenceExpression(
				new InvocationExpression(
					new MemberReferenceExpression(
						new TypeReferenceExpression { Type = new TypePattern(typeof(Type)).ToType() },
						"GetTypeFromHandle"),
					new TypeOfExpression(new AnyNode(groupName))
				), "TypeHandle");
		}

		public override bool DoMatch(INode? other, Match match)
		{
			return childNode.DoMatch(other, match);
		}

		public override string ToString()
		{
			return "typeof(...)";
		}
	}
}