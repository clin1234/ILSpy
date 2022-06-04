﻿// 
// UndocumentedExpression.cs
//  
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2010 Novell, Inc (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.


namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public enum UndocumentedExpressionType
	{
		ArgListAccess, // __arglist
		ArgList, // __arglist (a1, a2, ..., an)
		RefValue, // __refvalue (expr , type)
		RefType, // __reftype (expr)
		MakeRef // __makeref (expr)
	}

	/// <summary>
	/// Represents undocumented expressions.
	/// </summary>
	public sealed class UndocumentedExpression : Expression
	{
		public static readonly TokenRole ArglistKeywordRole = new("__arglist");
		public static readonly TokenRole RefvalueKeywordRole = new("__refvalue");
		public static readonly TokenRole ReftypeKeywordRole = new("__reftype");
		public static readonly TokenRole MakerefKeywordRole = new("__makeref");

		public UndocumentedExpressionType UndocumentedExpressionType {
			get;
			init;
		}

		public CSharpTokenNode UndocumentedToken {
			get {
				switch (UndocumentedExpressionType)
				{
					case UndocumentedExpressionType.ArgListAccess:
					case UndocumentedExpressionType.ArgList:
						return GetChildByRole(ArglistKeywordRole);
					case UndocumentedExpressionType.RefValue:
						return GetChildByRole(RefvalueKeywordRole);
					case UndocumentedExpressionType.RefType:
						return GetChildByRole(ReftypeKeywordRole);
					case UndocumentedExpressionType.MakeRef:
						return GetChildByRole(MakerefKeywordRole);
				}

				return CSharpTokenNode.Null;
			}
		}

		public CSharpTokenNode LParToken {
			get { return GetChildByRole(Roles.LPar); }
		}

		public AstNodeCollection<Expression> Arguments {
			get { return GetChildrenByRole(Roles.Argument); }
		}

		public CSharpTokenNode RParToken {
			get { return GetChildByRole(Roles.RPar); }
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitUndocumentedExpression(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitUndocumentedExpression(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitUndocumentedExpression(this, data);
		}

		protected internal override bool DoMatch(AstNode other, PatternMatching.Match match)
		{
			return other is UndocumentedExpression o &&
			       this.UndocumentedExpressionType == o.UndocumentedExpressionType &&
			       this.Arguments.DoMatch(o.Arguments, match);
		}
	}
}