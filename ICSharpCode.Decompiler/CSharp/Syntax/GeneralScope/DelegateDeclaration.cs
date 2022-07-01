﻿// 
// DelegateDeclaration.cs
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

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	/// <summary>
	/// delegate ReturnType Name&lt;TypeParameters&gt;(Parameters) where Constraints;
	/// </summary>
	public class DelegateDeclaration : EntityDeclaration
	{
		public override NodeType NodeType => NodeType.TypeDeclaration;

		public override SymbolKind SymbolKind => SymbolKind.TypeDefinition;

		public CSharpTokenNode DelegateToken => GetChildByRole(Roles.DelegateKeyword);

		public AstNodeCollection<TypeParameterDeclaration> TypeParameters => GetChildrenByRole(Roles.TypeParameter);

		public CSharpTokenNode LParToken => GetChildByRole(Roles.LPar);

		public AstNodeCollection<ParameterDeclaration> Parameters => GetChildrenByRole(Roles.Parameter);

		public CSharpTokenNode RParToken => GetChildByRole(Roles.RPar);

		public AstNodeCollection<Constraint> Constraints => GetChildrenByRole(Roles.Constraint);

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitDelegateDeclaration(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitDelegateDeclaration(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitDelegateDeclaration(this, data);
		}

		protected internal override bool DoMatch(AstNode other, PatternMatching.Match match)
		{
			return other is DelegateDeclaration o && MatchString(this.Name, o.Name)
				&& this.MatchAttributesAndModifiers(o, match) && this.ReturnType.DoMatch(o.ReturnType, match)
				&& this.TypeParameters.DoMatch(o.TypeParameters, match) && this.Parameters.DoMatch(o.Parameters, match)
				&& this.Constraints.DoMatch(o.Constraints, match);
		}
	}
}
