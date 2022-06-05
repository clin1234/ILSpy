// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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
using System.Collections.Generic;
using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.CSharp.TypeSystem
{
	/// <summary>
	/// Represents a scope that contains "using" statements.
	/// This is either the file itself, or a namespace declaration.
	/// </summary>
	[Serializable]
	public class UsingScope : AbstractFreezable
	{
		IList<string> externAliases;
		IList<KeyValuePair<string, TypeOrNamespaceReference>> usingAliases;
		IList<TypeOrNamespaceReference> usings;

		/// <summary>
		/// Creates a new root using scope.
		/// </summary>
		public UsingScope()
		{
		}

		/// <summary>
		/// Creates a new nested using scope.
		/// </summary>
		/// <param name="parent">The parent using scope.</param>
		/// <param name="shortName">The short namespace name.</param>
		public UsingScope(UsingScope parent, string shortName)
		{
			this.Parent = parent ?? throw new ArgumentNullException(nameof(parent));
			this.ShortNamespaceName = shortName ?? throw new ArgumentNullException(nameof(shortName));
		}

		public UsingScope Parent { get; }

		public string ShortNamespaceName { get; } = "";

		public string NamespaceName {
			get {
				if (Parent != null)
					return NamespaceDeclaration.BuildQualifiedName(Parent.NamespaceName, ShortNamespaceName);
				return ShortNamespaceName;
			}
			//			set {
			//				if (value == null)
			//					throw new ArgumentNullException("NamespaceName");
			//				FreezableHelper.ThrowIfFrozen(this);
			//				namespaceName = value;
			//			}
		}

		public IList<TypeOrNamespaceReference> Usings {
			get { return usings ??= new List<TypeOrNamespaceReference>(); }
		}

		public IList<KeyValuePair<string, TypeOrNamespaceReference>> UsingAliases {
			get { return usingAliases ??= new List<KeyValuePair<string, TypeOrNamespaceReference>>(); }
		}

		public IList<string> ExternAliases {
			get { return externAliases ??= new List<string>(); }
		}

		protected override void FreezeInternal()
		{
			usings = FreezableHelper.FreezeList(usings);
			usingAliases = FreezableHelper.FreezeList(usingAliases);
			externAliases = FreezableHelper.FreezeList(externAliases);

			// In current model (no child scopes), it makes sense to freeze the parent as well
			// to ensure the whole lookup chain is immutable.
			Parent?.Freeze();

			base.FreezeInternal();
		}

		/// <summary>
		/// Gets whether this using scope has an alias (either using or extern)
		/// with the specified name.
		/// </summary>
		public bool HasAlias(string identifier)
		{
			if (usingAliases != null)
			{
				if (usingAliases.Any(pair => pair.Key == identifier))
				{
					return true;
				}
			}

			return externAliases != null && externAliases.Contains(identifier);
		}

		/// <summary>
		/// Resolves the namespace represented by this using scope.
		/// </summary>
		public ResolvedUsingScope Resolve(ICompilation compilation)
		{
			CacheManager cache = compilation.CacheManager;
			if (cache.GetShared(this) is not ResolvedUsingScope resolved)
			{
				var csContext = new CSharpTypeResolveContext(compilation.MainModule, Parent?.Resolve(compilation));
				resolved = (ResolvedUsingScope)cache.GetOrAddShared(this, new ResolvedUsingScope(csContext, this));
			}

			return resolved;
		}
	}
}