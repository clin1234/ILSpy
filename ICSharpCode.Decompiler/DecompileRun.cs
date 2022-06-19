using System;
using System.Collections.Generic;
using System.Threading;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Resolver;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.TypeSystem;
using ICSharpCode.Decompiler.Documentation;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler
{
	public sealed class DecompileRun
	{
		public DecompileRun(DecompilerSettings settings)
		{
			this.Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		}

		public HashSet<string> DefinedSymbols { get; } = new();
		public HashSet<string> Namespaces { get; } = new();
		public CancellationToken CancellationToken { get; init; }
		public DecompilerSettings Settings { get; }
		public IDocumentationProvider? DocumentationProvider { get; init; }
		public Dictionary<ITypeDefinition, RecordDecompiler> RecordDecompilers { get; } = new();

		public Dictionary<ITypeDefinition, bool> TypeHierarchyIsKnown { get; } = new();

		private Lazy<UsingScope> usingScope => new(() => CreateUsingScope(Namespaces));

		public UsingScope UsingScope => usingScope.Value;

		internal EnumValueDisplayMode? EnumValueDisplayMode { get; set; }

		private static UsingScope CreateUsingScope(HashSet<string> requiredNamespacesSuperset)
		{
			UsingScope localUsingScope = new UsingScope();
			foreach (var ns in requiredNamespacesSuperset)
			{
				string[] parts = ns.Split('.');
				AstType nsType = new SimpleType(parts[0]);
				for (int i = 1; i < parts.Length; i++)
				{
					nsType = new MemberType { Target = nsType, MemberName = parts[i] };
				}

				if (nsType.ToTypeReference(NameLookupMode.TypeInUsingDeclaration) is TypeOrNamespaceReference reference)
				{
					localUsingScope.Usings.Add(reference);
				}
			}

			return localUsingScope;
		}
	}

	enum EnumValueDisplayMode
	{
		None,
		All,
		AllHex,
		FirstOnly
	}
}