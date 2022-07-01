﻿// 
// UnaryOperatorExpression.cs
//
// Author:
//       Mike Krüger <mkrueger@novell.com>
// 
// Copyright (c) 2009 Novell, Inc (http://www.novell.com)
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

using System;
using System.Linq.Expressions;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	/// <summary>
	/// Operator Expression
	/// </summary>
	public class UnaryOperatorExpression : Expression
	{
		public readonly static TokenRole NotRole = new("!");
		public readonly static TokenRole BitNotRole = new("~");
		public readonly static TokenRole MinusRole = new("-");
		public readonly static TokenRole PlusRole = new("+");
		public readonly static TokenRole IncrementRole = new("++");
		public readonly static TokenRole DecrementRole = new("--");
		public readonly static TokenRole DereferenceRole = new("*");
		public readonly static TokenRole AddressOfRole = new("&");
		public readonly static TokenRole AwaitRole = new("await");
		public readonly static TokenRole NullConditionalRole = new("?");
		public readonly static TokenRole SuppressNullableWarningRole = new("!");
		public readonly static TokenRole IndexFromEndRole = new("^");

		public UnaryOperatorExpression()
		{
		}

		public UnaryOperatorExpression(UnaryOperatorType op, Expression expression)
		{
			this.Operator = op;
			this.Expression = expression;
		}

		public UnaryOperatorType Operator {
			get;
			set;
		}

		public CSharpTokenNode OperatorToken => GetChildByRole(GetOperatorRole(Operator));

		public Expression Expression {
			get { return GetChildByRole(Roles.Expression); }
			set { SetChildByRole(Roles.Expression, value); }
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitUnaryOperatorExpression(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitUnaryOperatorExpression(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitUnaryOperatorExpression(this, data);
		}

		protected internal override bool DoMatch(AstNode other, PatternMatching.Match match)
		{
			return other is UnaryOperatorExpression o && (this.Operator == UnaryOperatorType.Any || this.Operator == o.Operator)
				&& this.Expression.DoMatch(o.Expression, match);
		}

		public static TokenRole GetOperatorRole(UnaryOperatorType op)
		{
			return op switch {
				UnaryOperatorType.Not => NotRole,
				UnaryOperatorType.BitNot => BitNotRole,
				UnaryOperatorType.Minus => MinusRole,
				UnaryOperatorType.Plus => PlusRole,
				UnaryOperatorType.Increment or UnaryOperatorType.PostIncrement => IncrementRole,
				UnaryOperatorType.PostDecrement or UnaryOperatorType.Decrement => DecrementRole,
				UnaryOperatorType.Dereference => DereferenceRole,
				UnaryOperatorType.AddressOf => AddressOfRole,
				UnaryOperatorType.Await => AwaitRole,
				UnaryOperatorType.NullConditional => NullConditionalRole,
				UnaryOperatorType.NullConditionalRewrap or UnaryOperatorType.IsTrue => null,// no syntax
				UnaryOperatorType.SuppressNullableWarning => SuppressNullableWarningRole,
				UnaryOperatorType.IndexFromEnd => IndexFromEndRole,
				_ => throw new NotSupportedException("Invalid value for UnaryOperatorType"),
			};
		}

		public static ExpressionType GetLinqNodeType(UnaryOperatorType op, bool checkForOverflow)
		{
			return op switch {
				UnaryOperatorType.Not => ExpressionType.Not,
				UnaryOperatorType.BitNot => ExpressionType.OnesComplement,
				UnaryOperatorType.Minus => checkForOverflow ? ExpressionType.NegateChecked : ExpressionType.Negate,
				UnaryOperatorType.Plus => ExpressionType.UnaryPlus,
				UnaryOperatorType.Increment => ExpressionType.PreIncrementAssign,
				UnaryOperatorType.Decrement => ExpressionType.PreDecrementAssign,
				UnaryOperatorType.PostIncrement => ExpressionType.PostIncrementAssign,
				UnaryOperatorType.PostDecrement => ExpressionType.PostDecrementAssign,
				UnaryOperatorType.Dereference or UnaryOperatorType.AddressOf or UnaryOperatorType.Await or UnaryOperatorType.SuppressNullableWarning or UnaryOperatorType.IndexFromEnd => ExpressionType.Extension,
				_ => throw new NotSupportedException("Invalid value for UnaryOperatorType"),
			};
		}
	}

	public enum UnaryOperatorType
	{
		/// <summary>
		/// Any unary operator (used in pattern matching)
		/// </summary>
		Any,

		/// <summary>Logical not (!a)</summary>
		Not,
		/// <summary>Bitwise not (~a)</summary>
		BitNot,
		/// <summary>Unary minus (-a)</summary>
		Minus,
		/// <summary>Unary plus (+a)</summary>
		Plus,
		/// <summary>Pre increment (++a)</summary>
		Increment,
		/// <summary>Pre decrement (--a)</summary>
		Decrement,
		/// <summary>Post increment (a++)</summary>
		PostIncrement,
		/// <summary>Post decrement (a--)</summary>
		PostDecrement,
		/// <summary>Dereferencing (*a)</summary>
		Dereference,
		/// <summary>Get address (&amp;a)</summary>
		AddressOf,
		/// <summary>C# 5.0 await</summary>
		Await,
		/// <summary>C# 6 null-conditional operator.
		/// Occurs as target of member reference or indexer expressions
		/// to indicate <c>?.</c> or <c>?[]</c>.
		/// Corresponds to <c>nullable.unwrap</c> in ILAst.
		/// </summary>
		NullConditional,
		/// <summary>
		/// Wrapper around a primary expression containing a null conditional operator.
		/// Corresponds to <c>nullable.rewrap</c> in ILAst.
		/// This has no syntax in C#, but the node is used to ensure parentheses are inserted where necessary.
		/// </summary>
		NullConditionalRewrap,
		/// <summary>
		/// Implicit call of "operator true".
		/// </summary>
		IsTrue,
		/// <summary>
		/// C# 8 postfix ! operator (dammit operator)
		/// </summary>
		SuppressNullableWarning,
		/// <summary>
		/// C# 8 prefix ^ operator
		/// </summary>
		IndexFromEnd,
	}
}
