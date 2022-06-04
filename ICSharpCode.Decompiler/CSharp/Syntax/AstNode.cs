#nullable enable
// 
// AstNode.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ICSharpCode.Decompiler.CSharp.OutputVisitor;
using ICSharpCode.Decompiler.CSharp.Syntax.PatternMatching;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.CSharp.Syntax
{
	public abstract class AstNode : AbstractAnnotatable, IFreezable, INode, ICloneable
	{
		// Derived classes may also use a few bits,
		// for example Identifier uses 1 bit for IsVerbatim

		const uint roleIndexMask = (1u << Role.RoleIndexBits) - 1;
		const uint frozenBit = 1u << Role.RoleIndexBits;

		protected const int AstNodeFlagsUsedBits = Role.RoleIndexBits + 1;

		// the Root role must be available when creating the null nodes, so we can't put it in the Roles class
		internal static readonly Role<AstNode?> RootRole = new("Root", null);

		// Flags, from least significant to most significant bits:
		// - Role.RoleIndexBits: role index
		// - 1 bit: IsFrozen
		protected uint flags = RootRole.Index;

		protected AstNode()
		{
			if (IsNull)
				Freeze();
		}

		public abstract NodeType NodeType {
			get;
		}

		public virtual TextLocation StartLocation {
			get {
				var child = FirstChild;
				if (child == null)
					return TextLocation.Empty;
				return child.StartLocation;
			}
		}

		public virtual TextLocation EndLocation {
			get {
				var child = LastChild;
				if (child == null)
					return TextLocation.Empty;
				return child.EndLocation;
			}
		}

		public AstNode? Parent { get; private set; }

		internal uint RoleIndex {
			get { return flags & roleIndexMask; }
		}

		public AstNode? NextSibling { get; private set; }

		public AstNode? PrevSibling { get; private set; }

		public AstNode? FirstChild { get; private set; }

		public AstNode? LastChild { get; private set; }

		public bool HasChildren {
			get {
				return FirstChild != null;
			}
		}

		public IEnumerable<AstNode> Children {
			get {
				AstNode? next;
				for (AstNode? cur = FirstChild; cur != null; cur = next)
				{
					Debug.Assert(cur.Parent == this);
					// Remember next before yielding cur.
					// This allows removing/replacing nodes while iterating through the list.
					next = cur.NextSibling;
					yield return cur;
				}
			}
		}

		/// <summary>
		/// Gets the ancestors of this node (excluding this node itself)
		/// </summary>
		public IEnumerable<AstNode> Ancestors {
			get {
				for (AstNode? cur = Parent; cur != null; cur = cur.Parent)
				{
					yield return cur;
				}
			}
		}

		/// <summary>
		/// Gets the ancestors of this node (including this node itself)
		/// </summary>
		public IEnumerable<AstNode> AncestorsAndSelf {
			get {
				for (AstNode? cur = this; cur != null; cur = cur.Parent)
				{
					yield return cur;
				}
			}
		}

		/// <summary>
		/// Gets all descendants of this node (excluding this node itself) in pre-order.
		/// </summary>
		public IEnumerable<AstNode> Descendants {
			get { return GetDescendantsImpl(false); }
		}

		/// <summary>
		/// Gets all descendants of this node (including this node itself) in pre-order.
		/// </summary>
		public IEnumerable<AstNode> DescendantsAndSelf {
			get { return GetDescendantsImpl(true); }
		}

		object ICloneable.Clone()
		{
			return Clone();
		}

		public bool IsFrozen {
			get { return (flags & frozenBit) != 0; }
		}

		public void Freeze()
		{
			if (!IsFrozen)
			{
				for (AstNode? child = FirstChild; child != null; child = child.NextSibling)
					child.Freeze();
				flags |= frozenBit;
			}
		}

		public virtual bool IsNull {
			get {
				return false;
			}
		}

		public Role Role {
			get {
				return Role.GetByIndex(flags & roleIndexMask);
			}
			set {
				ArgumentNullException.ThrowIfNull(value);
				if (!value.IsValid(this))
					throw new ArgumentException("This node is not valid in the new role.");
				ThrowIfFrozen();
				SetRole(value);
			}
		}

		protected void ThrowIfFrozen()
		{
			if (IsFrozen)
				throw new InvalidOperationException("Cannot mutate frozen " + GetType().Name);
		}

		void SetRole(Role role)
		{
			flags = (flags & ~roleIndexMask) | role.Index;
		}

		public IEnumerable<AstNode> DescendantNodes(Func<AstNode, bool>? descendIntoChildren = null)
		{
			return GetDescendantsImpl(false, descendIntoChildren);
		}

		public IEnumerable<AstNode> DescendantNodesAndSelf(Func<AstNode, bool>? descendIntoChildren = null)
		{
			return GetDescendantsImpl(true, descendIntoChildren);
		}


		IEnumerable<AstNode> GetDescendantsImpl(bool includeSelf, Func<AstNode, bool>? descendIntoChildren = null)
		{
			if (includeSelf)
			{
				yield return this;
				if (descendIntoChildren != null && !descendIntoChildren(this))
					yield break;
			}

			Stack<AstNode?> nextStack = new();
			nextStack.Push(null);
			AstNode? pos = FirstChild;
			while (pos != null)
			{
				// Remember next before yielding pos.
				// This allows removing/replacing nodes while iterating through the list.
				if (pos.NextSibling != null)
					nextStack.Push(pos.NextSibling);
				yield return pos;
				if (pos.FirstChild != null && (descendIntoChildren == null || descendIntoChildren(pos)))
					pos = pos.FirstChild;
				else
					pos = nextStack.Pop();
			}
		}

		/// <summary>
		/// Gets the first child with the specified role.
		/// Returns the role's null object if the child is not found.
		/// </summary>
		public T GetChildByRole<T>(Role<T> role) where T : AstNode?
		{
			ArgumentNullException.ThrowIfNull(role);
			uint roleIndex = role.Index;
			for (var cur = FirstChild; cur != null; cur = cur.NextSibling)
			{
				if ((cur.flags & roleIndexMask) == roleIndex)
					return (T)cur;
			}

			return role.NullObject;
		}

		public T? GetParent<T>() where T : AstNode
		{
			return Ancestors.OfType<T>().FirstOrDefault();
		}

		public AstNode? GetParent(Func<AstNode, bool>? pred)
		{
			return pred != null ? Ancestors.FirstOrDefault(pred) : Ancestors.FirstOrDefault();
		}

		public AstNodeCollection<T> GetChildrenByRole<T>(Role<T> role) where T : AstNode
		{
			return new AstNodeCollection<T>(this, role);
		}

		protected void SetChildByRole<T>(Role<T> role, T newChild) where T : AstNode
		{
			AstNode oldChild = GetChildByRole(role);
			if (oldChild.IsNull)
				AddChild(newChild, role);
			else
				oldChild.ReplaceWith(newChild);
		}

		public void AddChild<T>(T? child, Role<T> role) where T : AstNode
		{
			ArgumentNullException.ThrowIfNull(role);
			if (child == null || child.IsNull)
				return;
			ThrowIfFrozen();
			if (child == this)
				throw new ArgumentException("Cannot add a node to itself as a child.", nameof(child));
			if (child.Parent != null)
				throw new ArgumentException("Node is already used in another tree.", nameof(child));
			if (child.IsFrozen)
				throw new ArgumentException("Cannot add a frozen node.", nameof(child));
			AddChildUnsafe(child, role);
		}

		public void AddChildWithExistingRole(AstNode? child)
		{
			if (child == null || child.IsNull)
				return;
			ThrowIfFrozen();
			if (child == this)
				throw new ArgumentException("Cannot add a node to itself as a child.", nameof(child));
			if (child.Parent != null)
				throw new ArgumentException("Node is already used in another tree.", nameof(child));
			if (child.IsFrozen)
				throw new ArgumentException("Cannot add a frozen node.", nameof(child));
			AddChildUnsafe(child, child.Role);
		}

		/// <summary>
		/// Adds a child without performing any safety checks.
		/// </summary>
		internal void AddChildUnsafe(AstNode child, Role role)
		{
			child.Parent = this;
			child.SetRole(role);
			if (FirstChild == null)
			{
				LastChild = FirstChild = child;
			}
			else
			{
				LastChild!.NextSibling = child;
				child.PrevSibling = LastChild;
				LastChild = child;
			}
		}

		public void InsertChildBefore<T>(AstNode? nextSibling, T? child, Role<T> role) where T : AstNode
		{
			ArgumentNullException.ThrowIfNull(role);
			if (nextSibling == null || nextSibling.IsNull)
			{
				AddChild(child, role);
				return;
			}

			if (child == null || child.IsNull)
				return;
			ThrowIfFrozen();
			if (child.Parent != null)
				throw new ArgumentException("Node is already used in another tree.", nameof(child));
			if (child.IsFrozen)
				throw new ArgumentException("Cannot add a frozen node.", nameof(child));
			if (nextSibling.Parent != this)
				throw new ArgumentException("NextSibling is not a child of this node.", nameof(nextSibling));
			// No need to test for "Cannot add children to null nodes",
			// as there isn't any valid nextSibling in null nodes.
			InsertChildBeforeUnsafe(nextSibling, child, role);
		}

		internal void InsertChildBeforeUnsafe(AstNode nextSibling, AstNode child, Role role)
		{
			child.Parent = this;
			child.SetRole(role);
			child.NextSibling = nextSibling;
			child.PrevSibling = nextSibling.PrevSibling;

			if (nextSibling.PrevSibling != null)
			{
				Debug.Assert(nextSibling.PrevSibling.NextSibling == nextSibling);
				nextSibling.PrevSibling.NextSibling = child;
			}
			else
			{
				Debug.Assert(FirstChild == nextSibling);
				FirstChild = child;
			}

			nextSibling.PrevSibling = child;
		}

		public void InsertChildAfter<T>(AstNode? prevSibling, T child, Role<T> role) where T : AstNode
		{
			InsertChildBefore((prevSibling == null || prevSibling.IsNull) ? FirstChild : prevSibling.NextSibling, child,
				role);
		}

		/// <summary>
		/// Removes this node from its parent.
		/// </summary>
		public void Remove()
		{
			if (Parent != null)
			{
				ThrowIfFrozen();
				if (PrevSibling != null)
				{
					Debug.Assert(PrevSibling.NextSibling == this);
					PrevSibling.NextSibling = NextSibling;
				}
				else
				{
					Debug.Assert(Parent.FirstChild == this);
					Parent.FirstChild = NextSibling;
				}

				if (NextSibling != null)
				{
					Debug.Assert(NextSibling.PrevSibling == this);
					NextSibling.PrevSibling = PrevSibling;
				}
				else
				{
					Debug.Assert(Parent.LastChild == this);
					Parent.LastChild = PrevSibling;
				}

				Parent = null;
				PrevSibling = null;
				NextSibling = null;
			}
		}

		/// <summary>
		/// Replaces this node with the new node.
		/// </summary>
		public void ReplaceWith(AstNode? newNode)
		{
			if (newNode == null || newNode.IsNull)
			{
				Remove();
				return;
			}

			if (newNode == this)
				return; // nothing to do...
			if (Parent == null)
			{
				throw new InvalidOperationException(this.IsNull
					? "Cannot replace the null nodes"
					: "Cannot replace the root node");
			}

			ThrowIfFrozen();
			// Because this method doesn't statically check the new node's type with the role,
			// we perform a runtime test:
			if (!this.Role.IsValid(newNode))
			{
				throw new ArgumentException(
					$"The new node '{newNode.GetType().Name}' is not valid in the role {this.Role}", nameof(newNode));
			}

			if (newNode.Parent != null)
			{
				// newNode is used within this tree?
				if (newNode.Ancestors.Contains(this))
				{
					// e.g. "parenthesizedExpr.ReplaceWith(parenthesizedExpr.Expression);"
					// enable automatic removal
					newNode.Remove();
				}
				else
				{
					throw new ArgumentException("Node is already used in another tree.", nameof(newNode));
				}
			}

			if (newNode.IsFrozen)
				throw new ArgumentException("Cannot add a frozen node.", nameof(newNode));

			newNode.Parent = Parent;
			newNode.SetRole(this.Role);
			newNode.PrevSibling = PrevSibling;
			newNode.NextSibling = NextSibling;

			if (PrevSibling != null)
			{
				Debug.Assert(PrevSibling.NextSibling == this);
				PrevSibling.NextSibling = newNode;
			}
			else
			{
				Debug.Assert(Parent.FirstChild == this);
				Parent.FirstChild = newNode;
			}

			if (NextSibling != null)
			{
				Debug.Assert(NextSibling.PrevSibling == this);
				NextSibling.PrevSibling = newNode;
			}
			else
			{
				Debug.Assert(Parent.LastChild == this);
				Parent.LastChild = newNode;
			}

			Parent = null;
			PrevSibling = null;
			NextSibling = null;
		}

		public AstNode? ReplaceWith(Func<AstNode, AstNode?> replaceFunction)
		{
			ArgumentNullException.ThrowIfNull(replaceFunction);
			if (Parent == null)
			{
				throw new InvalidOperationException(this.IsNull
					? "Cannot replace the null nodes"
					: "Cannot replace the root node");
			}

			AstNode oldParent = Parent;
			AstNode? oldSuccessor = NextSibling;
			Role oldRole = this.Role;
			Remove();
			AstNode? replacement = replaceFunction(this);
			if (oldSuccessor != null && oldSuccessor.Parent != oldParent)
				throw new InvalidOperationException("replace function changed nextSibling of node being replaced?");
			if (!(replacement == null || replacement.IsNull))
			{
				if (replacement.Parent != null)
					throw new InvalidOperationException("replace function must return the root of a tree");
				if (!oldRole.IsValid(replacement))
				{
					throw new InvalidOperationException(
						$"The new node '{replacement.GetType().Name}' is not valid in the role {oldRole}");
				}

				if (oldSuccessor != null)
					oldParent.InsertChildBeforeUnsafe(oldSuccessor, replacement, oldRole);
				else
					oldParent.AddChildUnsafe(replacement, oldRole);
			}

			return replacement;
		}

		/// <summary>
		/// Clones the whole subtree starting at this AST node.
		/// </summary>
		/// <remarks>Annotations are copied over to the new nodes; and any annotations implementing ICloneable will be cloned.</remarks>
		public AstNode Clone()
		{
			AstNode copy = (AstNode)MemberwiseClone();
			// First, reset the shallow pointer copies
			copy.Parent = null;
			copy.FirstChild = null;
			copy.LastChild = null;
			copy.PrevSibling = null;
			copy.NextSibling = null;
			copy.flags &= ~frozenBit; // unfreeze the copy

			// Then perform a deep copy:
			for (AstNode? cur = FirstChild; cur != null; cur = cur.NextSibling)
			{
				copy.AddChildUnsafe(cur.Clone(), cur.Role);
			}

			// Finally, clone the annotation, if necessary
			copy.CloneAnnotations();

			return copy;
		}

		public abstract void AcceptVisitor(IAstVisitor visitor);

		public abstract T AcceptVisitor<T>(IAstVisitor<T> visitor);

		public abstract S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data);

		public AstNode? GetNextNode()
		{
			if (NextSibling != null)
				return NextSibling;
			return Parent?.GetNextNode();
		}

		/// <summary>
		/// Gets the next node which fullfills a given predicate
		/// </summary>
		/// <returns>The next node.</returns>
		/// <param name="pred">The predicate.</param>
		public AstNode? GetNextNode(Func<AstNode, bool> pred)
		{
			var next = GetNextNode();
			while (next != null && !pred(next))
				next = next.GetNextNode();
			return next;
		}

		public AstNode? GetPrevNode()
		{
			if (PrevSibling != null)
				return PrevSibling;
			return Parent?.GetPrevNode();
		}

		/// <summary>
		/// Gets the previous node which fullfills a given predicate
		/// </summary>
		/// <returns>The next node.</returns>
		/// <param name="pred">The predicate.</param>
		public AstNode? GetPrevNode(Func<AstNode, bool> pred)
		{
			var prev = GetPrevNode();
			while (prev != null && !pred(prev))
				prev = prev.GetPrevNode();
			return prev;
		}

		// filters all non c# nodes (comments, white spaces or pre processor directives)
		public AstNode? GetCSharpNodeBefore(AstNode node)
		{
			var n = node.PrevSibling;
			while (n != null)
			{
				if (n.Role != Roles.Comment)
					return n;
				n = n.GetPrevNode();
			}

			return null;
		}

		/// <summary>
		/// Gets the next sibling which fullfills a given predicate
		/// </summary>
		/// <returns>The next node.</returns>
		/// <param name="pred">The predicate.</param>
		public AstNode? GetNextSibling(Func<AstNode, bool> pred)
		{
			var next = NextSibling;
			while (next != null && !pred(next))
				next = next.NextSibling;
			return next;
		}

		/// <summary>
		/// Gets the next sibling which fullfills a given predicate
		/// </summary>
		/// <returns>The next node.</returns>
		/// <param name="pred">The predicate.</param>
		public AstNode? GetPrevSibling(Func<AstNode, bool> pred)
		{
			var prev = PrevSibling;
			while (prev != null && !pred(prev))
				prev = prev.PrevSibling;
			return prev;
		}


		/// <summary>
		/// Gets the node that fully contains the range from startLocation to endLocation.
		/// </summary>
		public AstNode GetNodeContaining(TextLocation startLocation, TextLocation endLocation)
		{
			for (AstNode? child = FirstChild; child != null; child = child.NextSibling)
			{
				if (child.StartLocation <= startLocation && endLocation <= child.EndLocation)
					return child.GetNodeContaining(startLocation, endLocation);
			}

			return this;
		}

		/// <summary>
		/// Returns the root nodes of all subtrees that are fully contained in the specified region.
		/// </summary>
		public IEnumerable<AstNode> GetNodesBetween(int startLine, int startColumn, int endLine, int endColumn)
		{
			return GetNodesBetween(new TextLocation(startLine, startColumn), new TextLocation(endLine, endColumn));
		}

		/// <summary>
		/// Returns the root nodes of all subtrees that are fully contained between <paramref name="start"/> and <paramref name="end"/> (inclusive).
		/// </summary>
		public IEnumerable<AstNode> GetNodesBetween(TextLocation start, TextLocation end)
		{
			AstNode? node = this;
			while (node != null)
			{
				AstNode? next;
				if (start <= node.StartLocation && node.EndLocation <= end)
				{
					// Remember next before yielding node.
					// This allows iteration to continue when the caller removes/replaces the node.
					next = node.GetNextNode();
					yield return node;
				}
				else
				{
					next = node.EndLocation <= start ? node.GetNextNode() : node.FirstChild;
				}

				if (next != null && next.StartLocation > end)
					yield break;
				node = next;
			}
		}

		/// <summary>
		/// Gets the node as formatted C# output.
		/// </summary>
		/// <param name='formattingOptions'>
		/// Formatting options.
		/// </param>
		public virtual string ToString(CSharpFormattingOptions? formattingOptions)
		{
			if (IsNull)
				return "";
			var w = new StringWriter();
			AcceptVisitor(new CSharpOutputVisitor(w, formattingOptions ?? FormattingOptionsFactory.CreateMono()));
			return w.ToString();
		}

		public sealed override string ToString()
		{
			return ToString(null);
		}

		/// <summary>
		/// Returns true, if the given coordinates (line, column) are in the node.
		/// </summary>
		/// <returns>
		/// True, if the given coordinates are between StartLocation and EndLocation (exclusive); otherwise, false.
		/// </returns>
		public bool Contains(int line, int column)
		{
			return Contains(new TextLocation(line, column));
		}

		/// <summary>
		/// Returns true, if the given coordinates are in the node.
		/// </summary>
		/// <returns>
		/// True, if location is between StartLocation and EndLocation (exclusive); otherwise, false.
		/// </returns>
		public bool Contains(TextLocation location)
		{
			return this.StartLocation <= location && location < this.EndLocation;
		}

		/// <summary>
		/// Returns true, if the given coordinates (line, column) are in the node.
		/// </summary>
		/// <returns>
		/// True, if the given coordinates are between StartLocation and EndLocation (inclusive); otherwise, false.
		/// </returns>
		public bool IsInside(int line, int column)
		{
			return IsInside(new TextLocation(line, column));
		}

		/// <summary>
		/// Returns true, if the given coordinates are in the node.
		/// </summary>
		/// <returns>
		/// True, if location is between StartLocation and EndLocation (inclusive); otherwise, false.
		/// </returns>
		public bool IsInside(TextLocation location)
		{
			return this.StartLocation <= location && location <= this.EndLocation;
		}

		public override void AddAnnotation(object annotation)
		{
			if (this.IsNull)
				throw new InvalidOperationException("Cannot add annotations to the null node");
			base.AddAnnotation(annotation);
		}

		internal string DebugToString()
		{
			if (IsNull)
				return "Null";
			string text = ToString();
			text = text.TrimEnd().Replace("\t", "").Replace(Environment.NewLine, " ");
			if (text.Length > 100)
				return text[..97] + "...";
			else
				return text;
		}

		#region Null

		public static readonly AstNode Null = new NullAstNode();

		sealed class NullAstNode : AstNode
		{
			public override NodeType NodeType {
				get {
					return NodeType.Unknown;
				}
			}

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

			protected internal override bool DoMatch(AstNode? other, Match match)
			{
				return other == null || other.IsNull;
			}
		}

		#endregion

		#region PatternPlaceholder

		public static implicit operator AstNode?(Pattern? pattern)
		{
			return pattern != null ? new PatternPlaceholder(pattern) : null;
		}

		sealed class PatternPlaceholder : AstNode, INode
		{
			readonly Pattern child;

			public PatternPlaceholder(Pattern child)
			{
				this.child = child;
			}

			public override NodeType NodeType {
				get { return NodeType.Pattern; }
			}

			bool INode.DoMatchCollection(Role? role, INode? pos, Match match, BacktrackingInfo backtrackingInfo)
			{
				return child.DoMatchCollection(role, pos, match, backtrackingInfo);
			}

			public override void AcceptVisitor(IAstVisitor visitor)
			{
				visitor.VisitPatternPlaceholder(this, child);
			}

			public override T AcceptVisitor<T>(IAstVisitor<T> visitor)
			{
				return visitor.VisitPatternPlaceholder(this, child);
			}

			public override S AcceptVisitor<T, S>(IAstVisitor<T, S> visitor, T data)
			{
				return visitor.VisitPatternPlaceholder(this, child, data);
			}

			protected internal override bool DoMatch(AstNode? other, Match match)
			{
				return child.DoMatch(other, match);
			}
		}

		#endregion

		#region Pattern Matching

		protected static bool MatchString(string? pattern, string? text)
		{
			return Pattern.MatchString(pattern, text);
		}

		protected internal abstract bool DoMatch(AstNode? other, Match match);

		bool INode.DoMatch(INode? other, Match match)
		{
			AstNode? o = other as AstNode;
			// try matching if other is null, or if other is an AstNode
			return (other == null || o != null) && DoMatch(o, match);
		}

		bool INode.DoMatchCollection(Role? role, INode? pos, Match match, BacktrackingInfo? backtrackingInfo)
		{
			AstNode? o = pos as AstNode;
			return (pos == null || o != null) && DoMatch(o, match);
		}

		INode? INode.NextSibling {
			get { return NextSibling; }
		}

		INode? INode.FirstChild {
			get { return FirstChild; }
		}

		#endregion

		#region GetNodeAt

		/// <summary>
		/// Gets the node specified by T at the location line, column. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End exclusive)
		/// </summary>
		public AstNode? GetNodeAt(int line, int column, Predicate<AstNode>? pred = null)
		{
			return GetNodeAt(new TextLocation(line, column), pred);
		}

		/// <summary>
		/// Gets the node specified by pred at location. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End exclusive)
		/// </summary>
		public AstNode? GetNodeAt(TextLocation location, Predicate<AstNode>? pred = null)
		{
			AstNode? result = null;
			AstNode node = this;
			while (node.LastChild != null)
			{
				var child = node.LastChild;
				while (child != null && child.StartLocation > location)
					child = child.PrevSibling;
				if (child != null && location < child.EndLocation)
				{
					if (pred == null || pred(child))
						result = child;
					node = child;
				}
				else
				{
					// found no better child node - therefore the parent is the right one.
					break;
				}
			}

			return result;
		}

		/// <summary>
		/// Gets the node specified by T at the location line, column. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End exclusive)
		/// </summary>
		public T? GetNodeAt<T>(int line, int column) where T : AstNode
		{
			return GetNodeAt<T>(new TextLocation(line, column));
		}

		/// <summary>
		/// Gets the node specified by T at location. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End exclusive)
		/// </summary>
		public T? GetNodeAt<T>(TextLocation location) where T : AstNode
		{
			T? result = null;
			AstNode node = this;
			while (node.LastChild != null)
			{
				var child = node.LastChild;
				while (child != null && child.StartLocation > location)
					child = child.PrevSibling;
				if (child != null && location < child.EndLocation)
				{
					if (child is T astNode)
						result = astNode;
					node = child;
				}
				else
				{
					// found no better child node - therefore the parent is the right one.
					break;
				}
			}

			return result;
		}

		#endregion

		#region GetAdjacentNodeAt

		/// <summary>
		/// Gets the node specified by pred at the location line, column. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End inclusive)
		/// </summary>
		public AstNode? GetAdjacentNodeAt(int line, int column, Predicate<AstNode>? pred = null)
		{
			return GetAdjacentNodeAt(new TextLocation(line, column), pred);
		}

		/// <summary>
		/// Gets the node specified by pred at location. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End inclusive)
		/// </summary>
		public AstNode? GetAdjacentNodeAt(TextLocation location, Predicate<AstNode>? pred = null)
		{
			AstNode? result = null;
			AstNode node = this;
			while (node.LastChild != null)
			{
				var child = node.LastChild;
				while (child != null && child.StartLocation > location)
					child = child.PrevSibling;
				if (child != null && location <= child.EndLocation)
				{
					if (pred == null || pred(child))
						result = child;
					node = child;
				}
				else
				{
					// found no better child node - therefore the parent is the right one.
					break;
				}
			}

			return result;
		}

		/// <summary>
		/// Gets the node specified by T at the location line, column. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End inclusive)
		/// </summary>
		public T? GetAdjacentNodeAt<T>(int line, int column) where T : AstNode
		{
			return GetAdjacentNodeAt<T>(new TextLocation(line, column));
		}

		/// <summary>
		/// Gets the node specified by T at location. This is useful for getting a specific node from the tree. For example searching
		/// the current method declaration.
		/// (End inclusive)
		/// </summary>
		public T? GetAdjacentNodeAt<T>(TextLocation location) where T : AstNode
		{
			T? result = null;
			AstNode node = this;
			while (node.LastChild != null)
			{
				var child = node.LastChild;
				while (child != null && child.StartLocation > location)
					child = child.PrevSibling;
				if (child != null && location <= child.EndLocation)
				{
					if (child is T t)
						result = t;
					node = child;
				}
				else
				{
					// found no better child node - therefore the parent is the right one.
					break;
				}
			}

			return result;
		}

		#endregion
	}
}