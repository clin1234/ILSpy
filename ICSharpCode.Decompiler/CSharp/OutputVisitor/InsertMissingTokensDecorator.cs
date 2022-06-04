﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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

namespace ICSharpCode.Decompiler.CSharp.OutputVisitor
{
	class InsertMissingTokensDecorator : DecoratingTokenWriter
	{
		readonly ILocatable locationProvider;
		readonly Stack<List<AstNode>> nodes = new();
		List<AstNode> currentList;

		public InsertMissingTokensDecorator(TokenWriter writer, ILocatable locationProvider)
			: base(writer)
		{
			this.locationProvider = locationProvider;
			currentList = new List<AstNode>();
		}

		public override void StartNode(AstNode node)
		{
			// ignore whitespace: these don't need to be processed.
			// StartNode/EndNode is only called for them to support folding of comments.
			if (node.NodeType != NodeType.Whitespace)
			{
				currentList.Add(node);
				nodes.Push(currentList);
				currentList = new List<AstNode>();
			}
			else if (node is Comment comment)
			{
				comment.SetStartLocation(locationProvider.Location);
			}

			if (node is ErrorExpression error)
			{
				error.Location = locationProvider.Location;
			}

			base.StartNode(node);
		}

		public override void EndNode(AstNode node)
		{
			// ignore whitespace: these don't need to be processed.
			// StartNode/EndNode is only called for them to support folding of comments.
			if (node.NodeType != NodeType.Whitespace)
			{
				System.Diagnostics.Debug.Assert(currentList != null);
				foreach (var removable in node.Children.Where(n => n is CSharpTokenNode))
				{
					removable.Remove();
				}

				foreach (var child in currentList)
				{
					System.Diagnostics.Debug.Assert(child.Parent == null || node == child.Parent);
					child.Remove();
					node.AddChildWithExistingRole(child);
				}

				currentList = nodes.Pop();
			}
			else if (node is Comment comment)
			{
				comment.SetEndLocation(locationProvider.Location);
			}

			base.EndNode(node);
		}

		public override void WriteToken(Role role, string token)
		{
			switch (nodes.Peek().LastOrDefault())
			{
				case EmptyStatement emptyStatement:
					emptyStatement.Location = locationProvider.Location;
					break;
				case ErrorExpression errorExpression:
					errorExpression.Location = locationProvider.Location;
					break;
				default:
					CSharpTokenNode t = new(locationProvider.Location, (TokenRole)role) {
						Role = role
					};
					currentList.Add(t);
					break;
			}

			base.WriteToken(role, token);
		}

		public override void WriteKeyword(Role role, string keyword)
		{
			TextLocation start = locationProvider.Location;
			CSharpTokenNode t = null;
			if (role is TokenRole tokenRole)
				t = new CSharpTokenNode(start, tokenRole);
			else if (role == EntityDeclaration.ModifierRole)
				t = new CSharpModifierToken(start, CSharpModifierToken.GetModifierValue(keyword));
			else if (keyword == "this")
			{
				if (nodes.Peek().LastOrDefault() is ThisReferenceExpression node)
					node.Location = start;
			}
			else if (keyword == "base")
			{
				if (nodes.Peek().LastOrDefault() is BaseReferenceExpression node)
					node.Location = start;
			}

			if (t != null)
			{
				currentList.Add(t);
				t.Role = role;
			}

			base.WriteKeyword(role, keyword);
		}

		public override void WriteIdentifier(Identifier identifier)
		{
			if (!identifier.IsNull)
				identifier.SetStartLocation(locationProvider.Location);
			currentList.Add(identifier);
			base.WriteIdentifier(identifier);
		}

		public override void WritePrimitiveValue(object value, LiteralFormat format = LiteralFormat.None)
		{
			Expression node = nodes.Peek().LastOrDefault() as Expression;
			var startLocation = locationProvider.Location;
			base.WritePrimitiveValue(value, format);
			if (node is PrimitiveExpression expression)
			{
				expression.SetLocation(startLocation, locationProvider.Location);
			}

			if (node is NullReferenceExpression referenceExpression)
			{
				referenceExpression.SetStartLocation(startLocation);
			}
		}

		public override void WritePrimitiveType(string type)
		{
			if (nodes.Peek().LastOrDefault() is PrimitiveType node)
				node.SetStartLocation(locationProvider.Location);
			base.WritePrimitiveType(type);
		}
	}
}