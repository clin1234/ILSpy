﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler
{
	internal class DecompileRun
	{
		public HashSet<string> DefinedSymbols { get; } = new();
		public HashSet<string> Namespaces { get; } = new();
		public CancellationToken CancellationToken { get; set; }
		public DecompilerSettings Settings { get; }
		public IDocumentationProvider DocumentationProvider { get; set; }
		public Dictionary<ITypeDefinition, RecordDecompiler> RecordDecompilers { get; } = new();

		public Dictionary<ITypeDefinition, bool> TypeHierarchyIsKnown { get; } = new();

		Lazy<CSharp.TypeSystem.UsingScope> usingScope => new(() => CreateUsingScope(Namespaces));

		public CSharp.TypeSystem.UsingScope UsingScope => usingScope.Value;

		public DecompileRun(DecompilerSettings settings)
		{
			this.Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		}

		static CSharp.TypeSystem.UsingScope CreateUsingScope(HashSet<string> requiredNamespacesSuperset)
		{
			var usingScope = new CSharp.TypeSystem.UsingScope();
			foreach (var ns in requiredNamespacesSuperset)
			{
				string[] parts = ns.Split('.');
				AstType nsType = new SimpleType(parts[0]);
				for (int i = 1; i < parts.Length; i++)
				{
					nsType = new MemberType { Target = nsType, MemberName = parts[i] };
				}
				if (nsType.ToTypeReference(CSharp.Resolver.NameLookupMode.TypeInUsingDeclaration) is CSharp.TypeSystem.TypeOrNamespaceReference reference)
					usingScope.Usings.Add(reference);
			}
			return usingScope;
		}

		public EnumValueDisplayMode? EnumValueDisplayMode { get; set; }
	}

	enum EnumValueDisplayMode
	{
		None,
		All,
		AllHex,
		FirstOnly
	}
}
