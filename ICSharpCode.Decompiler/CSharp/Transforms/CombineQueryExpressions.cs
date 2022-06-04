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

using System.Collections.Generic;
using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	/// <summary>
	/// Combines query expressions and removes transparent identifiers.
	/// </summary>
	public sealed class CombineQueryExpressions : IAstTransform
	{
		static readonly InvocationExpression castPattern = new() {
			Target = new MemberReferenceExpression {
				Target = new AnyNode("inExpr"),
				MemberName = "Cast",
				TypeArguments = { new AnyNode("targetType") }
			}
		};

		static readonly QuerySelectClause selectTransparentIdentifierPattern = new() {
			Expression = new AnonymousTypeCreateExpression {
				Initializers = {
					new Repeat(
						new Choice {
							new IdentifierExpression(Pattern.AnyString)
								.WithName("expr"), // name is equivalent to name = name
							new MemberReferenceExpression(new AnyNode(), Pattern.AnyString)
								.WithName("expr"), // expr.name is equivalent to name = expr.name
							new NamedExpression {
								Name = Pattern.AnyString,
								Expression = new AnyNode()
							}.WithName("expr")
						}
					) { MinCount = 1 }
				}
			}
		};

		public void Run(AstNode rootNode, TransformContext context)
		{
			if (!context.Settings.QueryExpressions)
				return;
			CombineQueries(rootNode, new Dictionary<string, object>());
		}

		void CombineQueries(AstNode node, Dictionary<string, object> fromOrLetIdentifiers)
		{
			AstNode next;
			for (AstNode child = node.FirstChild; child != null; child = next)
			{
				// store reference to next child before transformation
				next = child.NextSibling;
				CombineQueries(child, fromOrLetIdentifiers);
			}

			if (node is QueryExpression query)
			{
				QueryFromClause fromClause = (QueryFromClause)query.Clauses.First();
				if (fromClause.Expression is QueryExpression innerQuery)
				{
					if (TryRemoveTransparentIdentifier(query, fromClause, innerQuery, fromOrLetIdentifiers))
					{
						RemoveTransparentIdentifierReferences(query, fromOrLetIdentifiers);
					}
					else
					{
						QueryContinuationClause continuation = new() {
							PrecedingQuery = innerQuery.Detach(),
							Identifier = fromClause.Identifier
						};
						continuation.CopyAnnotationsFrom(fromClause);
						fromClause.ReplaceWith(continuation);
					}
				}
				else
				{
					Match m = castPattern.Match(fromClause.Expression);
					if (m.Success)
					{
						fromClause.Type = m.Get<AstType>("targetType").Single().Detach();
						fromClause.Expression = m.Get<Expression>("inExpr").Single().Detach();
					}
				}
			}
		}

		bool TryRemoveTransparentIdentifier(QueryExpression query, QueryFromClause fromClause,
			QueryExpression innerQuery, Dictionary<string, object> letClauses)
		{
			if (!CSharpDecompiler.IsTransparentIdentifier(fromClause.Identifier))
				return false;
			QuerySelectClause selectClause = innerQuery.Clauses.Last() as QuerySelectClause;
			Match match = selectTransparentIdentifierPattern.Match(selectClause);
			if (!match.Success)
				return false;

			// from * in (from x in ... select new { members of anonymous type }) ...
			// =>
			// from x in ... { let x = ... } ...
			fromClause.Remove();
			selectClause.Remove();
			// Move clauses from innerQuery to query
			QueryClause insertionPos = null;
			foreach (var clause in innerQuery.Clauses)
			{
				query.Clauses.InsertAfter(insertionPos, insertionPos = clause.Detach());
			}

			foreach (var expr in match.Get<Expression>("expr"))
			{
				switch (expr)
				{
					case IdentifierExpression identifier:
						letClauses[identifier.Identifier] = identifier.Annotation<ILVariableResolveResult>();
						break;
					case MemberReferenceExpression member:
						AddQueryLetClause(member.MemberName, member);
						break;
					case NamedExpression namedExpression:
						if (namedExpression.Expression is IdentifierExpression identifierExpression &&
						    namedExpression.Name == identifierExpression.Identifier)
						{
							letClauses[namedExpression.Name] =
								identifierExpression.Annotation<ILVariableResolveResult>();
							continue;
						}

						AddQueryLetClause(namedExpression.Name, namedExpression.Expression);
						break;
				}
			}

			return true;

			void AddQueryLetClause(string name, Expression expression)
			{
				QueryLetClause letClause = new() { Identifier = name, Expression = expression.Detach() };
				var annotation = new LetIdentifierAnnotation();
				letClause.AddAnnotation(annotation);
				letClauses[name] = annotation;
				query.Clauses.InsertAfter(insertionPos, letClause);
			}
		}

		/// <summary>
		/// Removes all occurrences of transparent identifiers
		/// </summary>
		void RemoveTransparentIdentifierReferences(AstNode node, Dictionary<string, object> fromOrLetIdentifiers)
		{
			foreach (AstNode child in node.Children)
			{
				RemoveTransparentIdentifierReferences(child, fromOrLetIdentifiers);
			}

			if (node is MemberReferenceExpression mre)
			{
				if (mre.Target is IdentifierExpression ident &&
				    CSharpDecompiler.IsTransparentIdentifier(ident.Identifier))
				{
					IdentifierExpression newIdent = new(mre.MemberName);
					mre.TypeArguments.MoveTo(newIdent.TypeArguments);
					newIdent.CopyAnnotationsFrom(mre);
					newIdent
						.RemoveAnnotations<
							Semantics.MemberResolveResult>(); // remove the reference to the property of the anonymous type
					if (fromOrLetIdentifiers.TryGetValue(mre.MemberName, out var annotation))
						newIdent.AddAnnotation(annotation);
					mre.ReplaceWith(newIdent);
				}
			}
		}
	}

	public sealed class LetIdentifierAnnotation
	{
	}
}