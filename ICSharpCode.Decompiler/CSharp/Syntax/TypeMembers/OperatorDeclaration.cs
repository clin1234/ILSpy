// 
// OperatorDeclaration.cs
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
using System.ComponentModel;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public enum OperatorType
	{
		// Unary operators
		LogicalNot,
		OnesComplement,
		Increment,
		Decrement,
		True,
		False,

		// Unary and Binary operators
		Addition,
		Subtraction,

		UnaryPlus,
		UnaryNegation,

		// Binary operators
		Multiply,
		Division,
		Modulus,
		BitwiseAnd,
		BitwiseOr,
		ExclusiveOr,
		LeftShift,
		RightShift,
		Equality,
		Inequality,
		GreaterThan,
		LessThan,
		GreaterThanOrEqual,
		LessThanOrEqual,

		// Implicit and Explicit
		Implicit,
		Explicit
	}

	public class OperatorDeclaration : EntityDeclaration
	{
		public static readonly TokenRole OperatorKeywordRole = new("operator");

		// Unary operators
		public static readonly TokenRole LogicalNotRole = new("!");
		public static readonly TokenRole OnesComplementRole = new("~");
		public static readonly TokenRole IncrementRole = new("++");
		public static readonly TokenRole DecrementRole = new("--");
		public static readonly TokenRole TrueRole = new("true");
		public static readonly TokenRole FalseRole = new("false");

		// Unary and Binary operators
		public static readonly TokenRole AdditionRole = new("+");
		public static readonly TokenRole SubtractionRole = new("-");

		// Binary operators
		public static readonly TokenRole MultiplyRole = new("*");
		public static readonly TokenRole DivisionRole = new("/");
		public static readonly TokenRole ModulusRole = new("%");
		public static readonly TokenRole BitwiseAndRole = new("&");
		public static readonly TokenRole BitwiseOrRole = new("|");
		public static readonly TokenRole ExclusiveOrRole = new("^");
		public static readonly TokenRole LeftShiftRole = new("<<");
		public static readonly TokenRole RightShiftRole = new(">>");
		public static readonly TokenRole EqualityRole = new("==");
		public static readonly TokenRole InequalityRole = new("!=");
		public static readonly TokenRole GreaterThanRole = new(">");
		public static readonly TokenRole LessThanRole = new("<");
		public static readonly TokenRole GreaterThanOrEqualRole = new(">=");
		public static readonly TokenRole LessThanOrEqualRole = new("<=");

		public static readonly TokenRole ExplicitRole = new("explicit");
		public static readonly TokenRole ImplicitRole = new("implicit");

		static readonly string[][] names;

		static OperatorDeclaration()
		{
			names = new string[(int)OperatorType.Explicit + 1][];
			names[(int)OperatorType.LogicalNot] = new[] { "!", "op_LogicalNot" };
			names[(int)OperatorType.OnesComplement] = new[] { "~", "op_OnesComplement" };
			names[(int)OperatorType.Increment] = new[] { "++", "op_Increment" };
			names[(int)OperatorType.Decrement] = new[] { "--", "op_Decrement" };
			names[(int)OperatorType.True] = new[] { "true", "op_True" };
			names[(int)OperatorType.False] = new[] { "false", "op_False" };
			names[(int)OperatorType.Addition] = new[] { "+", "op_Addition" };
			names[(int)OperatorType.Subtraction] = new[] { "-", "op_Subtraction" };
			names[(int)OperatorType.UnaryPlus] = new[] { "+", "op_UnaryPlus" };
			names[(int)OperatorType.UnaryNegation] = new[] { "-", "op_UnaryNegation" };
			names[(int)OperatorType.Multiply] = new[] { "*", "op_Multiply" };
			names[(int)OperatorType.Division] = new[] { "/", "op_Division" };
			names[(int)OperatorType.Modulus] = new[] { "%", "op_Modulus" };
			names[(int)OperatorType.BitwiseAnd] = new[] { "&", "op_BitwiseAnd" };
			names[(int)OperatorType.BitwiseOr] = new[] { "|", "op_BitwiseOr" };
			names[(int)OperatorType.ExclusiveOr] = new[] { "^", "op_ExclusiveOr" };
			names[(int)OperatorType.LeftShift] = new[] { "<<", "op_LeftShift" };
			names[(int)OperatorType.RightShift] = new[] { ">>", "op_RightShift" };
			names[(int)OperatorType.Equality] = new[] { "==", "op_Equality" };
			names[(int)OperatorType.Inequality] = new[] { "!=", "op_Inequality" };
			names[(int)OperatorType.GreaterThan] = new[] { ">", "op_GreaterThan" };
			names[(int)OperatorType.LessThan] = new[] { "<", "op_LessThan" };
			names[(int)OperatorType.GreaterThanOrEqual] = new[] { ">=", "op_GreaterThanOrEqual" };
			names[(int)OperatorType.LessThanOrEqual] = new[] { "<=", "op_LessThanOrEqual" };
			names[(int)OperatorType.Implicit] = new[] { "implicit", "op_Implicit" };
			names[(int)OperatorType.Explicit] = new[] { "explicit", "op_Explicit" };
		}

		public override SymbolKind SymbolKind => SymbolKind.Operator;

		OperatorType operatorType;

		public OperatorType OperatorType {
			get { return operatorType; }
			set {
				ThrowIfFrozen();
				operatorType = value;
			}
		}

		public CSharpTokenNode OperatorToken => GetChildByRole(OperatorKeywordRole);

		public CSharpTokenNode OperatorTypeToken => GetChildByRole(GetRole(OperatorType));

		public CSharpTokenNode LParToken => GetChildByRole(Roles.LPar);

		public AstNodeCollection<ParameterDeclaration> Parameters => GetChildrenByRole(Roles.Parameter);

		public CSharpTokenNode RParToken => GetChildByRole(Roles.RPar);

		public BlockStatement Body {
			get { return GetChildByRole(Roles.Body); }
			set { SetChildByRole(Roles.Body, value); }
		}

		/// <summary>
		/// Gets the operator type from the method name, or null, if the method does not represent one of the known operator types.
		/// </summary>
		public static OperatorType? GetOperatorType(string methodName)
		{
			for (int i = 0; i < names.Length; ++i)
			{
				if (names[i][1] == methodName)
					return (OperatorType)i;
			}

			return null;
		}

		public static TokenRole GetRole(OperatorType type)
		{
			return type switch {
				OperatorType.LogicalNot => LogicalNotRole,
				OperatorType.OnesComplement => OnesComplementRole,
				OperatorType.Increment => IncrementRole,
				OperatorType.Decrement => DecrementRole,
				OperatorType.True => TrueRole,
				OperatorType.False => FalseRole,
				OperatorType.Addition or OperatorType.UnaryPlus => AdditionRole,
				OperatorType.Subtraction or OperatorType.UnaryNegation => SubtractionRole,
				OperatorType.Multiply => MultiplyRole,
				OperatorType.Division => DivisionRole,
				OperatorType.Modulus => ModulusRole,
				OperatorType.BitwiseAnd => BitwiseAndRole,
				OperatorType.BitwiseOr => BitwiseOrRole,
				OperatorType.ExclusiveOr => ExclusiveOrRole,
				OperatorType.LeftShift => LeftShiftRole,
				OperatorType.RightShift => RightShiftRole,
				OperatorType.Equality => EqualityRole,
				OperatorType.Inequality => InequalityRole,
				OperatorType.GreaterThan => GreaterThanRole,
				OperatorType.LessThan => LessThanRole,
				OperatorType.GreaterThanOrEqual => GreaterThanOrEqualRole,
				OperatorType.LessThanOrEqual => LessThanOrEqualRole,
				OperatorType.Implicit => ImplicitRole,
				OperatorType.Explicit => ExplicitRole,
				_ => throw new ArgumentOutOfRangeException(),
			};
		}

		/// <summary>
		/// Gets the method name for the operator type. ("op_Addition", "op_Implicit", etc.)
		/// </summary>
		public static string GetName(OperatorType? type)
		{
			if (type == null)
				return null;
			return names[(int)type][1];
		}

		/// <summary>
		/// Gets the token for the operator type ("+", "implicit", etc.)
		/// </summary>
		public static string GetToken(OperatorType type)
		{
			return names[(int)type][0];
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitOperatorDeclaration(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitOperatorDeclaration(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitOperatorDeclaration(this, data);
		}

		public override string Name {
			get { return GetName(this.OperatorType); }
			set { throw new NotSupportedException(); }
		}

		[EditorBrowsable(EditorBrowsableState.Never)]
		public override Identifier NameToken {
			get { return Identifier.Null; }
			set { throw new NotSupportedException(); }
		}

		protected internal override bool DoMatch(AstNode other, PatternMatching.Match match)
		{
			return other is OperatorDeclaration o && this.MatchAttributesAndModifiers(o, match) && this.OperatorType == o.OperatorType
				&& this.ReturnType.DoMatch(o.ReturnType, match)
				&& this.Parameters.DoMatch(o.Parameters, match) && this.Body.DoMatch(o.Body, match);
		}
	}
}
