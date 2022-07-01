﻿// Copyright (c) 2010-2020 AlphaSierraPapa for the SharpDevelop Team
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

using ICSharpCode.Decompiler.CSharp.Syntax;

namespace ICSharpCode.Decompiler.CSharp.OutputVisitor
{
	/// <summary>
	/// Inserts the parentheses into the AST that are needed to ensure the AST can be printed correctly.
	/// For example, if the AST contains
	/// BinaryOperatorExpresson(2, Mul, BinaryOperatorExpression(1, Add, 1))); printing that AST
	/// would incorrectly result in "2 * 1 + 1". By running InsertParenthesesVisitor, the necessary
	/// parentheses are inserted: "2 * (1 + 1)".
	/// </summary>
	public class InsertParenthesesVisitor : DepthFirstAstVisitor
	{
		/// <summary>
		/// Gets/Sets whether the visitor should insert parentheses to make the code better looking.
		/// If this property is false, it will insert parentheses only where strictly required by the language spec.
		/// </summary>
		public bool InsertParenthesesForReadability { get; set; }

		enum PrecedenceLevel
		{
			// Higher integer value = higher precedence.
			Assignment,
			Conditional,    // ?:
			NullCoalescing, // ??
			ConditionalOr,  // ||
			ConditionalAnd, // &&
			BitwiseOr,      // |
			ExclusiveOr,    // binary ^
			BitwiseAnd,     // binary &
			Equality,       // == !=
			RelationalAndTypeTesting, // < <= > >= is
			Shift,          // << >>
			Additive,       // binary + -
			Multiplicative, // * / %
			Switch,         // C# 8 switch expression
			Range,          // ..
			Unary,
			QueryOrLambda,
			NullableRewrap,
			Primary
		}

		/// <summary>
		/// Gets the row number in the C# 4.0 spec operator precedence table.
		/// </summary>
		static PrecedenceLevel GetPrecedence(Expression expr)
		{
			// Note: the operator precedence table on MSDN is incorrect
			if (expr is QueryExpression or LambdaExpression)
			{
				// Not part of the table in the C# spec, but we need to ensure that queries within
				// primary expressions get parenthesized.
				return PrecedenceLevel.QueryOrLambda;
			}
			if (expr is UnaryOperatorExpression uoe)
			{
				return uoe.Operator switch {
					UnaryOperatorType.PostDecrement or UnaryOperatorType.PostIncrement or UnaryOperatorType.NullConditional or UnaryOperatorType.SuppressNullableWarning => PrecedenceLevel.Primary,
					UnaryOperatorType.NullConditionalRewrap => PrecedenceLevel.NullableRewrap,
					UnaryOperatorType.IsTrue => PrecedenceLevel.Conditional,
					_ => PrecedenceLevel.Unary,
				};
			}
			if (expr is CastExpression)
				return PrecedenceLevel.Unary;
			if (expr is PrimitiveExpression primitive)
			{
				var value = primitive.Value;
				if (value is int and < 0)
					return PrecedenceLevel.Unary;
				if (value is long and < 0)
					return PrecedenceLevel.Unary;
				if (value is float and < 0)
					return PrecedenceLevel.Unary;
				if (value is double and < 0)
					return PrecedenceLevel.Unary;
				if (value is decimal and < 0)
					return PrecedenceLevel.Unary;
				return PrecedenceLevel.Primary;
			}
			if (expr is BinaryOperatorExpression boe)
			{
				return boe.Operator switch {
					BinaryOperatorType.Range => PrecedenceLevel.Range,
					BinaryOperatorType.Multiply or BinaryOperatorType.Divide or BinaryOperatorType.Modulus => PrecedenceLevel.Multiplicative,
					BinaryOperatorType.Add or BinaryOperatorType.Subtract => PrecedenceLevel.Additive,
					BinaryOperatorType.ShiftLeft or BinaryOperatorType.ShiftRight => PrecedenceLevel.Shift,
					BinaryOperatorType.GreaterThan or BinaryOperatorType.GreaterThanOrEqual or BinaryOperatorType.LessThan or BinaryOperatorType.LessThanOrEqual => PrecedenceLevel.RelationalAndTypeTesting,
					BinaryOperatorType.Equality or BinaryOperatorType.InEquality => PrecedenceLevel.Equality,
					BinaryOperatorType.BitwiseAnd => PrecedenceLevel.BitwiseAnd,
					BinaryOperatorType.ExclusiveOr => PrecedenceLevel.ExclusiveOr,
					BinaryOperatorType.BitwiseOr => PrecedenceLevel.BitwiseOr,
					BinaryOperatorType.ConditionalAnd => PrecedenceLevel.ConditionalAnd,
					BinaryOperatorType.ConditionalOr => PrecedenceLevel.ConditionalOr,
					BinaryOperatorType.NullCoalescing => PrecedenceLevel.NullCoalescing,
					BinaryOperatorType.IsPattern => PrecedenceLevel.RelationalAndTypeTesting,
					_ => throw new NotSupportedException("Invalid value for BinaryOperatorType"),
				};
			}
			if (expr is SwitchExpression)
				return PrecedenceLevel.Switch;
			if (expr is IsExpression or AsExpression)
				return PrecedenceLevel.RelationalAndTypeTesting;
			if (expr is ConditionalExpression or DirectionExpression)
				return PrecedenceLevel.Conditional;
			if (expr is AssignmentExpression)
				return PrecedenceLevel.Assignment;
			// anything else: primary expression
			return PrecedenceLevel.Primary;
		}

		/// <summary>
		/// Parenthesizes the expression if it does not have the minimum required precedence.
		/// </summary>
		static void ParenthesizeIfRequired(Expression expr, PrecedenceLevel minimumPrecedence)
		{
			if (GetPrecedence(expr) < minimumPrecedence)
			{
				Parenthesize(expr);
			}
		}

		static void Parenthesize(Expression expr)
		{
			expr.ReplaceWith(e => new ParenthesizedExpression { Expression = e });
		}

		// Primary expressions
		public override void VisitMemberReferenceExpression(MemberReferenceExpression memberReferenceExpression)
		{
			ParenthesizeIfRequired(memberReferenceExpression.Target, PrecedenceLevel.Primary);
			base.VisitMemberReferenceExpression(memberReferenceExpression);
		}

		public override void VisitPointerReferenceExpression(PointerReferenceExpression pointerReferenceExpression)
		{
			ParenthesizeIfRequired(pointerReferenceExpression.Target, PrecedenceLevel.Primary);
			base.VisitPointerReferenceExpression(pointerReferenceExpression);
		}

		public override void VisitInvocationExpression(InvocationExpression invocationExpression)
		{
			ParenthesizeIfRequired(invocationExpression.Target, PrecedenceLevel.Primary);
			base.VisitInvocationExpression(invocationExpression);
		}

		public override void VisitIndexerExpression(IndexerExpression indexerExpression)
		{
			ParenthesizeIfRequired(indexerExpression.Target, PrecedenceLevel.Primary);
			switch (indexerExpression.Target)
			{
				case ArrayCreateExpression ace when InsertParenthesesForReadability || ace.Initializer.IsNull:
					// require parentheses for "(new int[1])[0]"
					Parenthesize(indexerExpression.Target);
					break;
				case StackAllocExpression sae when InsertParenthesesForReadability || sae.Initializer.IsNull:
					// require parentheses for "(stackalloc int[1])[0]"
					Parenthesize(indexerExpression.Target);
					break;
			}
			base.VisitIndexerExpression(indexerExpression);
		}

		// Unary expressions
		public override void VisitUnaryOperatorExpression(UnaryOperatorExpression unaryOperatorExpression)
		{
			ParenthesizeIfRequired(unaryOperatorExpression.Expression, GetPrecedence(unaryOperatorExpression));
			if (unaryOperatorExpression.Expression is UnaryOperatorExpression child && InsertParenthesesForReadability)
				Parenthesize(child);
			base.VisitUnaryOperatorExpression(unaryOperatorExpression);
		}

		public override void VisitCastExpression(CastExpression castExpression)
		{
			// Even in readability mode, don't parenthesize casts of casts.
			if (castExpression.Expression is not CastExpression)
			{
				ParenthesizeIfRequired(castExpression.Expression, InsertParenthesesForReadability ? PrecedenceLevel.NullableRewrap : PrecedenceLevel.Unary);
			}
			// There's a nasty issue in the C# grammar: cast expressions including certain operators are ambiguous in some cases
			// "(int)-1" is fine, but "(A)-b" is not a cast.
			if (castExpression.Expression is UnaryOperatorExpression uoe && !(uoe.Operator is UnaryOperatorType.BitNot or UnaryOperatorType.Not))
			{
				if (TypeCanBeMisinterpretedAsExpression(castExpression.Type))
				{
					Parenthesize(castExpression.Expression);
				}
			}
			// The above issue can also happen with PrimitiveExpressions representing negative values:
			PrimitiveExpression pe = castExpression.Expression as PrimitiveExpression;
			if (pe is { Value: { } } && TypeCanBeMisinterpretedAsExpression(castExpression.Type))
			{
				TypeCode typeCode = Type.GetTypeCode(pe.Value.GetType());
				switch (typeCode)
				{
					case TypeCode.SByte:
						if ((sbyte)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Int16:
						if ((short)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Int32:
						if ((int)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Int64:
						if ((long)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Single:
						if ((float)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Double:
						if ((double)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
					case TypeCode.Decimal:
						if ((decimal)pe.Value < 0)
							Parenthesize(castExpression.Expression);
						break;
				}
			}
			base.VisitCastExpression(castExpression);
		}

		static bool TypeCanBeMisinterpretedAsExpression(AstType type)
		{
			// SimpleTypes can always be misinterpreted as IdentifierExpressions
			// MemberTypes can be misinterpreted as MemberReferenceExpressions if they don't use double colon
			// PrimitiveTypes or ComposedTypes can never be misinterpreted as expressions.
			if (type is MemberType mt)
				return !mt.IsDoubleColon;
			else
				return type is SimpleType;
		}

		// Binary Operators
		public override void VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression)
		{
			PrecedenceLevel precedence = GetPrecedence(binaryOperatorExpression);
			if (binaryOperatorExpression.Operator == BinaryOperatorType.NullCoalescing)
			{
				if (InsertParenthesesForReadability)
				{
					ParenthesizeIfRequired(binaryOperatorExpression.Left, PrecedenceLevel.NullableRewrap);
					if (GetBinaryOperatorType(binaryOperatorExpression.Right) == BinaryOperatorType.NullCoalescing)
					{
						ParenthesizeIfRequired(binaryOperatorExpression.Right, precedence);
					}
					else
					{
						ParenthesizeIfRequired(binaryOperatorExpression.Right, PrecedenceLevel.NullableRewrap);
					}
				}
				else
				{
					// ?? is right-associative
					ParenthesizeIfRequired(binaryOperatorExpression.Left, precedence + 1);
					ParenthesizeIfRequired(binaryOperatorExpression.Right, precedence);
				}
			}
			else
			{
				if (InsertParenthesesForReadability && precedence < PrecedenceLevel.Equality)
				{
					// In readable mode, boost the priority of the left-hand side if the operator
					// there isn't the same as the operator on this expression.
					PrecedenceLevel boostTo = IsBitwise(binaryOperatorExpression.Operator) ? PrecedenceLevel.Unary : PrecedenceLevel.Equality;
					if (GetBinaryOperatorType(binaryOperatorExpression.Left) == binaryOperatorExpression.Operator)
					{
						ParenthesizeIfRequired(binaryOperatorExpression.Left, precedence);
					}
					else
					{
						ParenthesizeIfRequired(binaryOperatorExpression.Left, boostTo);
					}
					ParenthesizeIfRequired(binaryOperatorExpression.Right, boostTo);
				}
				else
				{
					// all other binary operators are left-associative
					ParenthesizeIfRequired(binaryOperatorExpression.Left, precedence);
					ParenthesizeIfRequired(binaryOperatorExpression.Right, precedence + 1);
				}
			}
			base.VisitBinaryOperatorExpression(binaryOperatorExpression);
		}

		static bool IsBitwise(BinaryOperatorType op)
		{
			return op is BinaryOperatorType.BitwiseAnd or BinaryOperatorType.BitwiseOr or BinaryOperatorType.ExclusiveOr;
		}

		static BinaryOperatorType? GetBinaryOperatorType(Expression expr)
		{
			if (expr is BinaryOperatorExpression boe)
				return boe.Operator;
			else
				return null;
		}

		public override void VisitIsExpression(IsExpression isExpression)
		{
			if (InsertParenthesesForReadability)
			{
				// few people know the precedence of 'is', so always put parentheses in nice-looking mode.
				ParenthesizeIfRequired(isExpression.Expression, PrecedenceLevel.NullableRewrap);
			}
			else
			{
				ParenthesizeIfRequired(isExpression.Expression, PrecedenceLevel.RelationalAndTypeTesting);
			}
			base.VisitIsExpression(isExpression);
		}

		public override void VisitAsExpression(AsExpression asExpression)
		{
			if (InsertParenthesesForReadability)
			{
				// few people know the precedence of 'as', so always put parentheses in nice-looking mode.
				ParenthesizeIfRequired(asExpression.Expression, PrecedenceLevel.NullableRewrap);
			}
			else
			{
				ParenthesizeIfRequired(asExpression.Expression, PrecedenceLevel.RelationalAndTypeTesting);
			}
			base.VisitAsExpression(asExpression);
		}

		// Conditional operator
		public override void VisitConditionalExpression(ConditionalExpression conditionalExpression)
		{
			// Inside of string interpolation ?: always needs parentheses.
			if (conditionalExpression.Parent is Interpolation)
			{
				Parenthesize(conditionalExpression);
			}

			// Associativity here is a bit tricky:
			// (a ? b : c ? d : e) == (a ? b : (c ? d : e))
			// (a ? b ? c : d : e) == (a ? (b ? c : d) : e)
			// Only ((a ? b : c) ? d : e) strictly needs the additional parentheses
			if (InsertParenthesesForReadability && !IsConditionalRefExpression(conditionalExpression))
			{
				// Precedence of ?: can be confusing; so always put parentheses in nice-looking mode.
				ParenthesizeIfRequired(conditionalExpression.Condition, PrecedenceLevel.NullableRewrap);
				ParenthesizeIfRequired(conditionalExpression.TrueExpression, PrecedenceLevel.NullableRewrap);
				ParenthesizeIfRequired(conditionalExpression.FalseExpression, PrecedenceLevel.NullableRewrap);
			}
			else
			{
				ParenthesizeIfRequired(conditionalExpression.Condition, PrecedenceLevel.Conditional + 1);
				ParenthesizeIfRequired(conditionalExpression.TrueExpression, PrecedenceLevel.Conditional);
				ParenthesizeIfRequired(conditionalExpression.FalseExpression, PrecedenceLevel.Conditional);
			}
			base.VisitConditionalExpression(conditionalExpression);
		}

		private static bool IsConditionalRefExpression(ConditionalExpression conditionalExpression)
		{
			return conditionalExpression.TrueExpression is DirectionExpression
				|| conditionalExpression.FalseExpression is DirectionExpression;
		}

		public override void VisitAssignmentExpression(AssignmentExpression assignmentExpression)
		{
			// assignment is right-associative
			ParenthesizeIfRequired(assignmentExpression.Left, PrecedenceLevel.Assignment + 1);
			HandleAssignmentRHS(assignmentExpression.Right);
			base.VisitAssignmentExpression(assignmentExpression);
		}

		private void HandleAssignmentRHS(Expression right)
		{
			if (InsertParenthesesForReadability && right is not DirectionExpression)
			{
				ParenthesizeIfRequired(right, PrecedenceLevel.Conditional + 1);
			}
			else
			{
				ParenthesizeIfRequired(right, PrecedenceLevel.Assignment);
			}
		}

		public override void VisitVariableInitializer(VariableInitializer variableInitializer)
		{
			if (!variableInitializer.Initializer.IsNull)
				HandleAssignmentRHS(variableInitializer.Initializer);
			base.VisitVariableInitializer(variableInitializer);
		}

		// don't need to handle lambdas, they have lowest precedence and unambiguous associativity
		public override void VisitQueryExpression(QueryExpression queryExpression)
		{
			// Query expressions are strange beasts:
			// "var a = -from b in c select d;" is valid, so queries bind stricter than unary expressions.
			// However, the end of the query is greedy. So their start sort of has a high precedence,
			// while their end has a very low precedence. We handle this by checking whether a query is used
			// as left part of a binary operator, and parenthesize it if required.
			HandleLambdaOrQuery(queryExpression);
			base.VisitQueryExpression(queryExpression);
		}

		public override void VisitLambdaExpression(LambdaExpression lambdaExpression)
		{
			// Lambdas are greedy in the same way as query expressions.
			HandleLambdaOrQuery(lambdaExpression);
			base.VisitLambdaExpression(lambdaExpression);
		}

		void HandleLambdaOrQuery(Expression expr)
		{
			if (expr.Role == BinaryOperatorExpression.LeftRole)
				Parenthesize(expr);
			if (expr.Parent is IsExpression or AsExpression)
				Parenthesize(expr);
			if (InsertParenthesesForReadability)
			{
				// when readability is desired, always parenthesize query expressions within unary or binary operators
				if (expr.Parent is UnaryOperatorExpression or BinaryOperatorExpression)
					Parenthesize(expr);
			}
		}

		public override void VisitNamedExpression(NamedExpression namedExpression)
		{
			if (InsertParenthesesForReadability)
			{
				ParenthesizeIfRequired(namedExpression.Expression, PrecedenceLevel.RelationalAndTypeTesting + 1);
			}
			base.VisitNamedExpression(namedExpression);
		}

		public override void VisitSwitchExpression(SwitchExpression switchExpression)
		{
			ParenthesizeIfRequired(switchExpression.Expression, PrecedenceLevel.Switch + 1);
			base.VisitSwitchExpression(switchExpression);
		}
	}
}
