// Copyright (c) 2013 Daniel Grunwald
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
	/// <summary>
	/// Extension methods for the syntax tree.
	/// </summary>
	public static class SyntaxExtensions
	{
		public static bool IsComparisonOperator(this OperatorType operatorType)
		{
			return operatorType switch {
				OperatorType.Equality or OperatorType.Inequality or OperatorType.GreaterThan or OperatorType.LessThan or OperatorType.GreaterThanOrEqual or OperatorType.LessThanOrEqual => true,
				_ => false,
			};
		}

		/// <summary>
		/// Returns true if <paramref name="operatorType"/> is bitwise and, bitwise or, or exclusive or.
		/// </summary>
		public static bool IsBitwise(this BinaryOperatorType operatorType)
		{
			return operatorType is BinaryOperatorType.BitwiseAnd or BinaryOperatorType.BitwiseOr or BinaryOperatorType.ExclusiveOr;
		}

		public static Statement GetNextStatement(this Statement statement)
		{
			AstNode next = statement.NextSibling;
			while (next != null && next is not Statement)
				next = next.NextSibling;
			return (Statement)next;
		}

		public static bool IsArgList(this AstType type)
		{
			var simpleType = type as SimpleType;
			return simpleType is { Identifier: "__arglist" };
		}

		public static void AddNamedArgument(this Attribute attribute, string name, Expression argument)
		{
			attribute.Arguments.Add(new AssignmentExpression(new IdentifierExpression(name), argument));
		}

		public static T Detach<T>(this T node) where T : AstNode
		{
			node.Remove();
			return node;
		}

		public static Expression UnwrapInDirectionExpression(this Expression expr)
		{
			if (!(expr is DirectionExpression { FieldDirection: FieldDirection.In } dir))
				return expr;
			return dir.Expression.Detach();
		}
	}
}
