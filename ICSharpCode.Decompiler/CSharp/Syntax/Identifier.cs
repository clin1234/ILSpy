﻿// 
// Identifier.cs
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

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public class Identifier : AstNode
	{
		const uint verbatimBit = 1u << AstNodeFlagsUsedBits;
		public new static readonly Identifier? Null = new NullIdentifier();

		string? name;

		TextLocation startLocation;

		Identifier()
		{
			this.name = string.Empty;
		}

		protected Identifier(string name, TextLocation location)
		{
			this.Name = name ?? throw new ArgumentNullException(nameof(name));
			this.startLocation = location;
		}

		public override NodeType NodeType {
			get {
				return NodeType.Token;
			}
		}

		public string? Name {
			get { return this.name; }
			set {
				if (value is null) throw new ArgumentNullException(nameof(value));
				ThrowIfFrozen();
				this.name = value;
			}
		}

		public override TextLocation StartLocation {
			get {
				return startLocation;
			}
		}

		public bool IsVerbatim {
			get {
				return (flags & verbatimBit) != 0;
			}
			init {
				ThrowIfFrozen();
				if (value)
					flags |= verbatimBit;
				else
					flags &= ~verbatimBit;
			}
		}

		public override TextLocation EndLocation {
			get {
				return new TextLocation(StartLocation.Line,
					StartLocation.Column + (Name ?? "").Length + (IsVerbatim ? 1 : 0));
			}
		}

		internal void SetStartLocation(TextLocation value)
		{
			ThrowIfFrozen();
			this.startLocation = value;
		}

		public static Identifier? Create(string? name)
		{
			return Create(name, TextLocation.Empty);
		}

		public static Identifier? Create(string? name, TextLocation location)
		{
			if (string.IsNullOrEmpty(name))
				return Null;
			if (name[0] == '@')
				return new Identifier(name[1..], new TextLocation(location.Line, location.Column + 1))
					{ IsVerbatim = true };
			return new Identifier(name, location);
		}

		public static Identifier? Create(string? name, TextLocation location, bool isVerbatim)
		{
			if (string.IsNullOrEmpty(name))
				return Null;

			if (isVerbatim)
				return new Identifier(name, location) { IsVerbatim = true };
			return new Identifier(name, location);
		}

		public override void AcceptVisitor(IAstVisitor visitor)
		{
			visitor.VisitIdentifier(this);
		}

		public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
		{
			return visitor.VisitIdentifier(this);
		}

		public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
		{
			return visitor.VisitIdentifier(this, data);
		}

		protected internal override bool DoMatch(AstNode? other, PatternMatching.Match match)
		{
			return other is Identifier { IsNull: false } o && MatchString(this.Name, o.Name);
		}

		sealed class NullIdentifier : Identifier
		{
			public override bool IsNull {
				get {
					return true;
				}
			}

			public override void AcceptVisitor(IAstVisitor visitor)
			{
				visitor.VisitNullNode(this);
			}

			public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
			{
				return visitor.VisitNullNode(this);
			}

			public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
			{
				return visitor.VisitNullNode(this, data);
			}

			protected internal override bool DoMatch(AstNode? other, PatternMatching.Match match)
			{
				return other == null || other.IsNull;
			}
		}
	}
}