﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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

using System.Linq;
using System.Reflection;

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;
using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	/// <summary>
	/// Replaces method calls with the appropriate operator expressions.
	/// </summary>
	internal sealed class ReplaceMethodCallsWithOperators : DepthFirstAstVisitor, IAstTransform
	{
		static readonly MemberReferenceExpression typeHandleOnTypeOfPattern = new() {
			Target = new Choice {
				new TypeOfExpression(new AnyNode()),
				new UndocumentedExpression
					{ UndocumentedExpressionType = UndocumentedExpressionType.RefType, Arguments = { new AnyNode() } }
			},
			MemberName = "TypeHandle"
		};

		static readonly Pattern ToStringCallPattern = new Choice {
			// target.ToString()
			new InvocationExpression(new MemberReferenceExpression(new AnyNode("target"), "ToString")).WithName("call"),
			// target?.ToString()
			new UnaryOperatorExpression(
				UnaryOperatorType.NullConditionalRewrap,
				new InvocationExpression(
					new MemberReferenceExpression(
						new UnaryOperatorExpression(UnaryOperatorType.NullConditional, new AnyNode("target")),
						"ToString")
				).WithName("call")
			).WithName("nullConditional")
		};

		static readonly Expression getMethodOrConstructorFromHandlePattern =
			new CastExpression(new Choice {
				new TypePattern(typeof(MethodInfo)),
				new TypePattern(typeof(ConstructorInfo))
			}, new InvocationExpression(
				new MemberReferenceExpression(new TypeReferenceExpression(new TypePattern(typeof(MethodBase)).ToType()),
					"GetMethodFromHandle"),
				new NamedNode("ldtokenNode",
					new MemberReferenceExpression(new LdTokenPattern("method").ToExpression(), "MethodHandle")),
				new OptionalNode(new MemberReferenceExpression(new TypeOfExpression(new AnyNode("declaringType")),
					"TypeHandle"))
			));

		TransformContext context;

		void IAstTransform.Run(AstNode rootNode, TransformContext context)
		{
			try
			{
				this.context = context;
				rootNode.AcceptVisitor(this);
			}
			finally
			{
				this.context = null;
			}
		}

		public override void VisitInvocationExpression(InvocationExpression invocationExpression)
		{
			base.VisitInvocationExpression(invocationExpression);
			ProcessInvocationExpression(invocationExpression);
		}

		void ProcessInvocationExpression(InvocationExpression? invocationExpression)
		{
			if (invocationExpression.GetSymbol() is not IMethod method)
				return;
			var arguments = invocationExpression.Arguments.ToArray();

			// Reduce "String.Concat(a, b)" to "a + b"
			if (IsStringConcat(method) && context.Settings.StringConcat && CheckArgumentsForStringConcat(arguments))
			{
				bool isInExpressionTree = invocationExpression.Ancestors.OfType<LambdaExpression>().Any(
					lambda => lambda.Annotation<IL.ILFunction>()?.Kind == IL.ILFunctionKind.ExpressionTree);
				Expression? arg0 = arguments[0].Detach();
				Expression? arg1 = arguments[1].Detach();
				if (!isInExpressionTree)
				{
					arg1 = RemoveRedundantToStringInConcat(arg1, method, isLastArgument: arguments.Length == 2)
						.Detach();
					if (arg1.GetResolveResult().Type.IsKnownType(KnownTypeCode.String))
					{
						arg0 = RemoveRedundantToStringInConcat(arg0, method, isLastArgument: false).Detach();
					}
				}

				var expr = new BinaryOperatorExpression(arg0, BinaryOperatorType.Add, arg1);
				for (int i = 2; i < arguments.Length; i++)
				{
					var arg = arguments[i].Detach();
					if (!isInExpressionTree)
					{
						arg = RemoveRedundantToStringInConcat(arg, method, isLastArgument: i == arguments.Length - 1)
							.Detach();
					}

					expr = new BinaryOperatorExpression(expr, BinaryOperatorType.Add, arg);
				}

				expr.CopyAnnotationsFrom(invocationExpression);
				invocationExpression.ReplaceWith(expr);
				return;
			}

			switch (method.FullName)
			{
				case "System.Type.GetTypeFromHandle":
					if (arguments.Length == 1)
					{
						if (typeHandleOnTypeOfPattern.IsMatch(arguments[0]))
						{
							Expression? target = ((MemberReferenceExpression)arguments[0]).Target;
							target.CopyInstructionsFrom(invocationExpression);
							invocationExpression.ReplaceWith(target);
							return;
						}
					}

					break;
				/*
			case "System.Reflection.FieldInfo.GetFieldFromHandle":
				// TODO : This is dead code because LdTokenAnnotation is not added anywhere:
				if (arguments.Length == 1) {
					MemberReferenceExpression mre = arguments[0] as MemberReferenceExpression;
					if (mre != null && mre.MemberName == "FieldHandle" && mre.Target.Annotation<LdTokenAnnotation>() != null) {
						invocationExpression.ReplaceWith(mre.Target);
						return;
					}
				} else if (arguments.Length == 2) {
					MemberReferenceExpression mre1 = arguments[0] as MemberReferenceExpression;
					MemberReferenceExpression mre2 = arguments[1] as MemberReferenceExpression;
					if (mre1 != null && mre1.MemberName == "FieldHandle" && mre1.Target.Annotation<LdTokenAnnotation>() != null) {
						if (mre2 != null && mre2.MemberName == "TypeHandle" && mre2.Target is TypeOfExpression) {
							Expression oldArg = ((InvocationExpression)mre1.Target).Arguments.Single();
							FieldReference field = oldArg.Annotation<FieldReference>();
							if (field != null) {
								AstType declaringType = ((TypeOfExpression)mre2.Target).Type.Detach();
								oldArg.ReplaceWith(new MemberReferenceExpression(new TypeReferenceExpression(declaringType), field.Name).CopyAnnotationsFrom(oldArg));
								invocationExpression.ReplaceWith(mre1.Target);
								return;
							}
						}
					}
				}
				break;
				*/
				case "System.Activator.CreateInstance":
					if (arguments.Length == 0 && method.TypeArguments.Count == 1 &&
					    IsInstantiableTypeParameter(method.TypeArguments[0]))
					{
						invocationExpression.ReplaceWith(
							new ObjectCreateExpression(
								context.TypeSystemAstBuilder.ConvertType(method.TypeArguments.First())));
					}

					break;
				case "System.Runtime.CompilerServices.RuntimeHelpers.GetSubArray":
					if (arguments.Length == 2 && context.Settings.Ranges)
					{
						var slicing = new IndexerExpression(arguments[0].Detach(), arguments[1].Detach());
						slicing.CopyAnnotationsFrom(invocationExpression);
						invocationExpression.ReplaceWith(slicing);
					}

					break;
			}

			BinaryOperatorType? bop = GetBinaryOperatorTypeFromMetadataName(method.Name);
			if (bop != null && arguments.Length == 2)
			{
				invocationExpression.Arguments.Clear(); // detach arguments from invocationExpression
				invocationExpression.ReplaceWith(
					new BinaryOperatorExpression(
						arguments[0].UnwrapInDirectionExpression(),
						bop.Value,
						arguments[1].UnwrapInDirectionExpression()
					).CopyAnnotationsFrom(invocationExpression)
				);
				return;
			}

			UnaryOperatorType? uop = GetUnaryOperatorTypeFromMetadataName(method.Name);
			if (uop != null && arguments.Length == 1)
			{
				if (uop is UnaryOperatorType.Increment or UnaryOperatorType.Decrement)
				{
					// `op_Increment(a)` is not equivalent to `++a`,
					// because it doesn't assign the incremented value to a.
					if (method.DeclaringType.IsKnownType(KnownTypeCode.Decimal))
					{
						// Legacy csc optimizes "d + 1m" to "op_Increment(d)",
						// so reverse that optimization here:
						invocationExpression.ReplaceWith(
							new BinaryOperatorExpression(
								arguments[0].UnwrapInDirectionExpression().Detach(),
								(uop == UnaryOperatorType.Increment
									? BinaryOperatorType.Add
									: BinaryOperatorType.Subtract),
								new PrimitiveExpression(1m)
							).CopyAnnotationsFrom(invocationExpression)
						);
					}

					return;
				}

				arguments[0].Remove(); // detach argument
				invocationExpression.ReplaceWith(
					new UnaryOperatorExpression(uop.Value, arguments[0].UnwrapInDirectionExpression())
						.CopyAnnotationsFrom(invocationExpression)
				);
				return;
			}

			switch (method.Name)
			{
				case "op_Explicit" when arguments.Length == 1:
					arguments[0].Remove(); // detach argument
					invocationExpression.ReplaceWith(
						new CastExpression(context.TypeSystemAstBuilder.ConvertType(method.ReturnType),
								arguments[0].UnwrapInDirectionExpression())
							.CopyAnnotationsFrom(invocationExpression)
					);
					return;
				case "op_True" when arguments.Length == 1 && invocationExpression.Role == Roles.Condition:
					invocationExpression.ReplaceWith(arguments[0].UnwrapInDirectionExpression());
					return;
			}
		}

		bool IsInstantiableTypeParameter(IType type)
		{
			return type is ITypeParameter { HasDefaultConstructorConstraint: true };
		}

		bool CheckArgumentsForStringConcat(Expression?[] arguments)
		{
			if (arguments.Length < 2)
				return false;

			if (arguments.Any(arg => arg is NamedArgumentExpression))
				return false;

			// The evaluation order when the object.ToString() calls happen is a mess:
			// The C# spec says the evaluation for order for each individual string + should be:
			//   * evaluate left argument
			//   * evaluate right argument
			//   * call ToString() on object argument
			// What actually happens pre-VS2019.3:
			//   * evaluate all arguments in chain of + operators from left to right
			//   * call ToString() on all object arguments from left to right
			// What happens in VS2019.3:
			//   * for each argument in chain of + operators fom left to right:
			//       * evaluate argument
			//       * call ToString() on object argument
			// See https://github.com/dotnet/roslyn/issues/38641 for details.
			// To ensure the decompiled code's behavior matches the original IL behavior,
			// no matter which compiler is used to recompile it, we require that all
			// implicit ToString() calls except for the last are free of side effects.
			foreach (var arg in arguments.SkipLast(1))
			{
				if (!ToStringIsKnownEffectFree(arg.GetResolveResult().Type))
				{
					return false;
				}
			}

			foreach (var arg in arguments)
			{
				if (arg.GetResolveResult() is InvocationResolveResult rr && IsStringConcat(rr.Member))
				{
					// Roslyn + mcs also flatten nested string.Concat() invocations within a operator+ use,
					// which causes it to use the incorrect evaluation order despite the code using an
					// explicit string.Concat() call.
					// This problem is avoided if the outer call remains string.Concat() as well.
					return false;
				}
			}

			// One of the first two arguments must be string, otherwise the + operator
			// won't resolve to a string concatenation.
			return arguments[0].GetResolveResult().Type.IsKnownType(KnownTypeCode.String)
			       || arguments[1].GetResolveResult().Type.IsKnownType(KnownTypeCode.String);
		}

		private bool IsStringConcat(IParameterizedMember member)
		{
			return member is IMethod { Name: "Concat" } method &&
			       method.DeclaringType.IsKnownType(KnownTypeCode.String);
		}

		internal static Expression RemoveRedundantToStringInConcat(Expression expr, IMethod concatMethod,
			bool isLastArgument)
		{
			var m = ToStringCallPattern.Match(expr);
			if (!m.Success)
				return expr;

			if (!concatMethod.Parameters.All(IsStringParameter))
			{
				// If we're using a string.Concat() overload involving object parameters,
				// string.Concat() itself already calls ToString() so the C# compiler shouldn't
				// generate additional ToString() calls in this case.
				return expr;
			}

			var toStringMethod = m.Get<Expression>("call").Single().GetSymbol() as IMethod;
			var target = m.Get<Expression>("target").Single();
			var type = target.GetResolveResult().Type;
			if (!(isLastArgument || ToStringIsKnownEffectFree(type)))
			{
				// ToString() order of evaluation matters, see CheckArgumentsForStringConcat().
				return expr;
			}

			if (type.IsReferenceType != false && !m.Has("nullConditional"))
			{
				// ToString() might throw NullReferenceException, but the builtin operator+ doesn't.
				return expr;
			}

			if (!ToStringIsKnownEffectFree(type) && toStringMethod != null &&
			    IL.Transforms.ILInlining.MethodRequiresCopyForReadonlyLValue(toStringMethod))
			{
				// ToString() on a struct may mutate the struct.
				// For operator+ the C# compiler creates a temporary copy before implicitly calling ToString(),
				// whereas an explicit ToString() call would mutate the original lvalue.
				// So we can't remove the compiler-generated ToString() call in cases where this might make a difference.
				return expr;
			}

			// All checks succeeded, we can eliminate the ToString() call.
			// The C# compiler will generate an equivalent call if the code is recompiled.
			return target;

			static bool IsStringParameter(IParameter p)
			{
				IType ty = p.Type;
				if (p.IsParams && ty.Kind == TypeKind.Array)
					ty = ((ArrayType)ty).ElementType;
				return ty.IsKnownType(KnownTypeCode.String);
			}
		}

		static bool ToStringIsKnownEffectFree(IType type)
		{
			type = NullableType.GetUnderlyingType(type);
			switch (type.GetDefinition()?.KnownTypeCode)
			{
				case KnownTypeCode.Boolean:
				case KnownTypeCode.Char:
				case KnownTypeCode.SByte:
				case KnownTypeCode.Byte:
				case KnownTypeCode.Int16:
				case KnownTypeCode.UInt16:
				case KnownTypeCode.Int32:
				case KnownTypeCode.UInt32:
				case KnownTypeCode.Int64:
				case KnownTypeCode.UInt64:
				case KnownTypeCode.Single:
				case KnownTypeCode.Double:
				case KnownTypeCode.Decimal:
				case KnownTypeCode.IntPtr:
				case KnownTypeCode.UIntPtr:
				case KnownTypeCode.String:
					return true;
				default:
					return false;
			}
		}

		static BinaryOperatorType? GetBinaryOperatorTypeFromMetadataName(string name)
		{
			return name switch {
				"op_Addition" => BinaryOperatorType.Add,
				"op_Subtraction" => BinaryOperatorType.Subtract,
				"op_Multiply" => BinaryOperatorType.Multiply,
				"op_Division" => BinaryOperatorType.Divide,
				"op_Modulus" => BinaryOperatorType.Modulus,
				"op_BitwiseAnd" => BinaryOperatorType.BitwiseAnd,
				"op_BitwiseOr" => BinaryOperatorType.BitwiseOr,
				"op_ExclusiveOr" => BinaryOperatorType.ExclusiveOr,
				"op_LeftShift" => BinaryOperatorType.ShiftLeft,
				"op_RightShift" => BinaryOperatorType.ShiftRight,
				"op_Equality" => BinaryOperatorType.Equality,
				"op_Inequality" => BinaryOperatorType.InEquality,
				"op_LessThan" => BinaryOperatorType.LessThan,
				"op_LessThanOrEqual" => BinaryOperatorType.LessThanOrEqual,
				"op_GreaterThan" => BinaryOperatorType.GreaterThan,
				"op_GreaterThanOrEqual" => BinaryOperatorType.GreaterThanOrEqual,
				_ => null
			};
		}

		static UnaryOperatorType? GetUnaryOperatorTypeFromMetadataName(string name)
		{
			return name switch {
				"op_LogicalNot" => UnaryOperatorType.Not,
				"op_OnesComplement" => UnaryOperatorType.BitNot,
				"op_UnaryNegation" => UnaryOperatorType.Minus,
				"op_UnaryPlus" => UnaryOperatorType.Plus,
				"op_Increment" => UnaryOperatorType.Increment,
				"op_Decrement" => UnaryOperatorType.Decrement,
				_ => null
			};
		}

		public override void VisitCastExpression(CastExpression castExpression)
		{
			base.VisitCastExpression(castExpression);
			// Handle methodof
			Match m = getMethodOrConstructorFromHandlePattern.Match(castExpression);
			if (m.Success)
			{
				IMethod method = m.Get<AstNode>("method").Single().GetSymbol() as IMethod;
				if (m.Has("declaringType") && method != null)
				{
					Expression newNode = new MemberReferenceExpression(
						new TypeReferenceExpression(m.Get<AstType>("declaringType").Single().Detach()), method.Name);
					newNode = new InvocationExpression(newNode,
						method.Parameters.Select(p =>
							new TypeReferenceExpression(context.TypeSystemAstBuilder.ConvertType(p.Type))));
					m.Get<AstNode>("method").Single().ReplaceWith(newNode);
				}

				castExpression.ReplaceWith(m.Get<AstNode>("ldtokenNode").Single().CopyAnnotationsFrom(castExpression));
			}
		}
	}
}