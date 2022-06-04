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

using System;
using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	/// <summary>
	/// Decompiles query expressions.
	/// Based on C# 4.0 spec, §7.16.2 Query expression translation
	/// </summary>
	internal sealed class IntroduceQueryExpressions : IAstTransform
	{
		TransformContext context;

		public void Run(AstNode rootNode, TransformContext context)
		{
			if (!context.Settings.QueryExpressions)
				return;
			this.context = context;
			DecompileQueries(rootNode);
			// After all queries were decompiled, detect degenerate queries (queries not property terminated with 'select' or 'group')
			// and fix them, either by adding a degenerate select, or by combining them with another query.
			foreach (QueryExpression query in rootNode.Descendants.OfType<QueryExpression>())
			{
				QueryFromClause fromClause = (QueryFromClause)query.Clauses.First();
				if (IsDegenerateQuery(query))
				{
					// introduce select for degenerate query
					query.Clauses.Add(new QuerySelectClause {
						Expression = new IdentifierExpression(fromClause.Identifier).CopyAnnotationsFrom(fromClause)
					});
				}

				// See if the data source of this query is a degenerate query,
				// and combine the queries if possible.
				QueryExpression innerQuery = fromClause.Expression as QueryExpression;
				while (IsDegenerateQuery(innerQuery))
				{
					QueryFromClause innerFromClause = (QueryFromClause)innerQuery.Clauses.First();
					if (fromClause.Identifier != innerFromClause.Identifier)
						break;
					// Replace the fromClause with all clauses from the inner query
					fromClause.Remove();
					QueryClause insertionPos = null;
					foreach (var clause in innerQuery.Clauses)
					{
						query.Clauses.InsertAfter(insertionPos, insertionPos = clause.Detach());
					}

					fromClause = innerFromClause;
					innerQuery = fromClause.Expression as QueryExpression;
				}
			}
		}

		bool IsDegenerateQuery(QueryExpression query)
		{
			if (query == null)
				return false;
			var lastClause = query.Clauses.LastOrDefault();
			return lastClause is not (QuerySelectClause or QueryGroupClause);
		}

		void DecompileQueries(AstNode node)
		{
			Expression query = DecompileQuery(node as InvocationExpression);
			if (query != null)
			{
				if (node.Parent is ExpressionStatement && CanUseDiscardAssignment())
					query = new AssignmentExpression(new IdentifierExpression("_"), query);
				node.ReplaceWith(query);
			}

			AstNode next;
			for (AstNode child = (query ?? node).FirstChild; child != null; child = next)
			{
				// store reference to next child before transformation
				next = child.NextSibling;
				DecompileQueries(child);
			}
		}

		bool CanUseDiscardAssignment()
		{
			// TODO : check whether there exists a variable named '_' in scope.
			return context.Settings.Discards;
		}

		QueryExpression DecompileQuery(InvocationExpression invocation)
		{
			if (invocation?.Target is not MemberReferenceExpression mre || IsNullConditional(mre.Target))
				return null;
			switch (mre.MemberName)
			{
				case "Select":
				{
					if (invocation.Arguments.Count != 1)
						return null;
					if (!IsComplexQuery(mre))
						return null;
					Expression expr = invocation.Arguments.Single();
					if (MatchSimpleLambda(expr, out ParameterDeclaration parameter, out Expression body))
					{
						QueryExpression query = new();
						query.Clauses.Add(MakeFromClause(parameter, mre.Target.Detach()));
						query.Clauses.Add(new QuerySelectClause
								{ Expression = WrapExpressionInParenthesesIfNecessary(body.Detach(), parameter.Name) }
							.CopyAnnotationsFrom(expr));
						return query;
					}

					return null;
				}
				case "GroupBy":
				{
					switch (invocation.Arguments.Count)
					{
						case 2:
						{
							Expression keyLambda = invocation.Arguments.ElementAt(0);
							Expression projectionLambda = invocation.Arguments.ElementAt(1);
							if (MatchSimpleLambda(keyLambda, out ParameterDeclaration parameter1,
								    out Expression keySelector)
							    && MatchSimpleLambda(projectionLambda, out ParameterDeclaration parameter2,
								    out Expression elementSelector)
							    && parameter1.Name == parameter2.Name)
							{
								QueryExpression query = new();
								query.Clauses.Add(MakeFromClause(parameter1, mre.Target.Detach()));
								var queryGroupClause = new QueryGroupClause {
									Projection = elementSelector.Detach(),
									Key = keySelector.Detach()
								};
								queryGroupClause.AddAnnotation(new QueryGroupClauseAnnotation(
									keyLambda.Annotation<IL.ILFunction>(),
									projectionLambda.Annotation<IL.ILFunction>()));
								query.Clauses.Add(queryGroupClause);
								return query;
							}

							break;
						}
						case 1:
						{
							Expression lambda = invocation.Arguments.Single();
							if (MatchSimpleLambda(lambda, out ParameterDeclaration parameter,
								    out Expression keySelector))
							{
								QueryExpression query = new();
								query.Clauses.Add(MakeFromClause(parameter, mre.Target.Detach()));
								query.Clauses.Add(new QueryGroupClause {
									Projection =
										new IdentifierExpression(parameter.Name).CopyAnnotationsFrom(parameter),
									Key = keySelector.Detach()
								});
								return query;
							}

							break;
						}
					}

					return null;
				}
				case "SelectMany":
				{
					if (invocation.Arguments.Count != 2)
						return null;
					var fromExpressionLambda = invocation.Arguments.ElementAt(0);
					if (!MatchSimpleLambda(fromExpressionLambda, out ParameterDeclaration parameter,
						    out Expression collectionSelector))
						return null;
					if (IsNullConditional(collectionSelector))
						return null;
					if (invocation.Arguments.ElementAt(1) is LambdaExpression lambda && lambda.Parameters.Count == 2 &&
					    lambda.Body is Expression expression)
					{
						ParameterDeclaration p1 = lambda.Parameters.ElementAt(0);
						ParameterDeclaration p2 = lambda.Parameters.ElementAt(1);
						if (p1.Name == parameter.Name)
						{
							QueryExpression query = new();
							query.Clauses.Add(MakeFromClause(p1, mre.Target.Detach()));
							query.Clauses.Add(MakeFromClause(p2, collectionSelector.Detach())
								.CopyAnnotationsFrom(fromExpressionLambda));
							query.Clauses.Add(new QuerySelectClause {
								Expression = WrapExpressionInParenthesesIfNecessary(expression.Detach(), parameter.Name)
							});
							return query;
						}
					}

					return null;
				}
				case "Where":
				{
					if (invocation.Arguments.Count != 1)
						return null;
					if (!IsComplexQuery(mre))
						return null;
					Expression expr = invocation.Arguments.Single();
					if (MatchSimpleLambda(expr, out ParameterDeclaration parameter, out Expression body))
					{
						QueryExpression query = new();
						query.Clauses.Add(MakeFromClause(parameter, mre.Target.Detach()));
						query.Clauses.Add(new QueryWhereClause { Condition = body.Detach() }.CopyAnnotationsFrom(expr));
						return query;
					}

					return null;
				}
				case "OrderBy":
				case "OrderByDescending":
				case "ThenBy":
				case "ThenByDescending":
				{
					if (invocation.Arguments.Count != 1)
						return null;
					if (!IsComplexQuery(mre))
						return null;
					var lambda = invocation.Arguments.Single();
					if (MatchSimpleLambda(lambda, out ParameterDeclaration parameter, out Expression orderExpression))
					{
						if (ValidateThenByChain(invocation, parameter.Name))
						{
							QueryOrderClause orderClause = new();
							while (mre.MemberName is "ThenBy" or "ThenByDescending")
							{
								// insert new ordering at beginning
								orderClause.Orderings.InsertAfter(
									null, new QueryOrdering {
										Expression = orderExpression.Detach(),
										Direction = (mre.MemberName == "ThenBy"
											? QueryOrderingDirection.None
											: QueryOrderingDirection.Descending)
									}.CopyAnnotationsFrom(lambda));

								InvocationExpression tmp = (InvocationExpression)mre.Target;
								mre = (MemberReferenceExpression)tmp.Target;
								lambda = tmp.Arguments.Single();
								MatchSimpleLambda(lambda, out parameter, out orderExpression);
							}

							// insert new ordering at beginning
							orderClause.Orderings.InsertAfter(
								null, new QueryOrdering {
									Expression = orderExpression.Detach(),
									Direction = (mre.MemberName == "OrderBy"
										? QueryOrderingDirection.None
										: QueryOrderingDirection.Descending)
								}.CopyAnnotationsFrom(lambda));

							QueryExpression query = new();
							query.Clauses.Add(MakeFromClause(parameter, mre.Target.Detach()));
							query.Clauses.Add(orderClause);
							return query;
						}
					}

					return null;
				}
				case "Join":
				case "GroupJoin":
				{
					if (invocation.Arguments.Count != 4)
						return null;
					Expression source1 = mre.Target;
					Expression source2 = invocation.Arguments.ElementAt(0);
					if (IsNullConditional(source2))
						return null;
					Expression outerLambda = invocation.Arguments.ElementAt(1);
					if (!MatchSimpleLambda(outerLambda, out ParameterDeclaration element1, out Expression key1))
						return null;
					Expression innerLambda = invocation.Arguments.ElementAt(2);
					if (!MatchSimpleLambda(innerLambda, out ParameterDeclaration element2, out Expression key2))
						return null;
					if (invocation.Arguments.ElementAt(3) is LambdaExpression lambda && lambda.Parameters.Count == 2 &&
					    lambda.Body is Expression expression)
					{
						ParameterDeclaration p1 = lambda.Parameters.ElementAt(0);
						ParameterDeclaration p2 = lambda.Parameters.ElementAt(1);
						if (ValidateParameter(p1) && ValidateParameter(p2)
						                          && p1.Name == element1.Name && (p2.Name == element2.Name ||
							                          mre.MemberName == "GroupJoin"))
						{
							QueryExpression query = new();
							query.Clauses.Add(MakeFromClause(element1, source1.Detach()));
							QueryJoinClause joinClause = new() {
								JoinIdentifier = element2.Name // join elementName2
							};
							joinClause.JoinIdentifierToken.CopyAnnotationsFrom(element2);
							joinClause.InExpression = source2.Detach(); // in source2
							joinClause.OnExpression = key1.Detach(); // on key1
							joinClause.EqualsExpression = key2.Detach(); // equals key2
							if (mre.MemberName == "GroupJoin")
							{
								joinClause.IntoIdentifier = p2.Name; // into p2.Name
								joinClause.IntoIdentifierToken.CopyAnnotationsFrom(p2);
							}

							joinClause.AddAnnotation(new QueryJoinClauseAnnotation(
								outerLambda.Annotation<IL.ILFunction>(), innerLambda.Annotation<IL.ILFunction>()));
							query.Clauses.Add(joinClause);
							query.Clauses.Add(new QuerySelectClause { Expression = expression.Detach() }
								.CopyAnnotationsFrom(lambda));
							return query;
						}
					}

					return null;
				}
				default:
					return null;
			}
		}

		static bool IsComplexQuery(MemberReferenceExpression mre)
		{
			return ((mre.Target is InvocationExpression && mre.Parent is InvocationExpression) ||
			        mre.Parent?.Parent is QueryClause);
		}

		QueryFromClause MakeFromClause(ParameterDeclaration parameter, Expression body)
		{
			QueryFromClause fromClause = new() {
				Identifier = parameter.Name,
				Expression = body
			};
			fromClause.CopyAnnotationsFrom(parameter);
			return fromClause;
		}

		bool IsNullConditional(Expression target)
		{
			return target is UnaryOperatorExpression { Operator: UnaryOperatorType.NullConditional };
		}

		/// <summary>
		/// This fixes #437: Decompilation of query expression loses material parentheses
		/// We wrap the expression in parentheses if:
		/// - the Select-call is explicit (see caller(s))
		/// - the expression is a plain identifier matching the parameter name
		/// </summary>
		Expression WrapExpressionInParenthesesIfNecessary(Expression expression, string parameterName)
		{
			if (expression is IdentifierExpression ident &&
			    parameterName.Equals(ident.Identifier, StringComparison.Ordinal))
				return new ParenthesizedExpression(expression);
			return expression;
		}

		/// <summary>
		/// Ensure that all ThenBy's are correct, and that the list of ThenBy's is terminated by an 'OrderBy' invocation.
		/// </summary>
		bool ValidateThenByChain(InvocationExpression invocation, string expectedParameterName)
		{
			if (invocation == null || invocation.Arguments.Count != 1)
				return false;
			if (invocation.Target is not MemberReferenceExpression mre)
				return false;
			if (!MatchSimpleLambda(invocation.Arguments.Single(), out ParameterDeclaration parameter, out _))
				return false;
			if (parameter.Name != expectedParameterName)
				return false;

			return mre.MemberName switch {
				"OrderBy" or "OrderByDescending" => !IsNullConditional(mre.Target),
				"ThenBy" or "ThenByDescending" => ValidateThenByChain(mre.Target as InvocationExpression,
					expectedParameterName),
				_ => false
			};
		}

		/// <summary>Matches simple lambdas of the form "a => b"</summary>
		bool MatchSimpleLambda(Expression expr, out ParameterDeclaration parameter, out Expression body)
		{
			if (expr is LambdaExpression lambda && lambda.Parameters.Count == 1 && lambda.Body is Expression expression)
			{
				ParameterDeclaration p = lambda.Parameters.Single();
				if (ValidateParameter(p))
				{
					parameter = p;
					body = expression;
					return true;
				}
			}

			parameter = null;
			body = null;
			return false;
		}

		private static bool ValidateParameter(ParameterDeclaration p)
		{
			return p.ParameterModifier == ParameterModifier.None && p.Attributes.Count == 0;
		}
	}
}