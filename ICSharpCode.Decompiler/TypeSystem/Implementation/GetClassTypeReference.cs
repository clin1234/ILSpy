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

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// Type Reference used when the fully qualified type name is known.
	/// </summary>
	[Serializable]
	public sealed class GetClassTypeReference : ITypeReference, ISupportsInterning
	{
		readonly FullTypeName fullTypeName;
		readonly bool? isReferenceType;

		/// <summary>
		/// Creates a new GetClassTypeReference that searches a type definition.
		/// </summary>
		/// <param name="fullTypeName">The full name of the type.</param>
		/// <param name="module">A reference to the module containing this type.
		/// If this parameter is null, the GetClassTypeReference will search in all
		/// assemblies belonging to the compilation.
		/// </param>
		public GetClassTypeReference(FullTypeName fullTypeName, IModuleReference? module = null,
			bool? isReferenceType = null)
		{
			this.fullTypeName = fullTypeName;
			Module = module;
			this.isReferenceType = isReferenceType;
		}

		/// <summary>
		/// Creates a new GetClassTypeReference that searches a top-level type in all assemblies.
		/// </summary>
		/// <param name="namespaceName">The namespace name containing the type, e.g. "System.Collections.Generic".</param>
		/// <param name="name">The name of the type, e.g. "List".</param>
		/// <param name="typeParameterCount">The number of type parameters, (e.g. 1 for List&lt;T&gt;).</param>
		public GetClassTypeReference(string namespaceName, string name, int typeParameterCount = 0,
			bool? isReferenceType = null)
		{
			this.fullTypeName = new TopLevelTypeName(namespaceName, name, typeParameterCount);
			this.isReferenceType = isReferenceType;
		}

		/// <summary>
		/// Creates a new GetClassTypeReference that searches a top-level type in the specified assembly.
		/// </summary>
		/// <param name="module">A reference to the assembly containing this type.
		/// If this parameter is null, the GetClassTypeReference will search in all assemblies belonging to the ICompilation.</param>
		/// <param name="namespaceName">The namespace name containing the type, e.g. "System.Collections.Generic".</param>
		/// <param name="name">The name of the type, e.g. "List".</param>
		/// <param name="typeParameterCount">The number of type parameters, (e.g. 1 for List&lt;T&gt;).</param>
		public GetClassTypeReference(IModuleReference? module, string namespaceName, string name,
			int typeParameterCount = 0, bool? isReferenceType = null)
		{
			Module = module;
			this.fullTypeName = new TopLevelTypeName(namespaceName, name, typeParameterCount);
			this.isReferenceType = isReferenceType;
		}

		/// <summary>
		/// Gets the assembly reference.
		/// This property returns null if the GetClassTypeReference is searching in all assemblies
		/// of the compilation.
		/// </summary>
		public IModuleReference? Module { get; }

		/// <summary>
		/// Gets the full name of the type this reference is searching for.
		/// </summary>
		public FullTypeName FullTypeName { get { return fullTypeName; } }

		public IType Resolve(ITypeResolveContext context)
		{
			ArgumentNullException.ThrowIfNull(context);

			IType type = null;
			if (Module == null)
			{
				// No assembly specified: look in all assemblies, but prefer the current assembly
				if (context.CurrentModule != null)
				{
					type = context.CurrentModule.GetTypeDefinition(fullTypeName);
				}

				type ??= ResolveInAllAssemblies(context.Compilation, in fullTypeName);
			}
			else
			{
				// Assembly specified: only look in the specified assembly.
				// But if that's not loaded in the compilation, allow fall back to other assemblies.
				// (the non-loaded assembly might be a facade containing type forwarders -
				//  for example, when referencing a portable library from a non-portable project)
				IModule? asm = Module.Resolve(context);
				type = asm?.GetTypeDefinition(fullTypeName) ?? ResolveInAllAssemblies(context.Compilation, in fullTypeName);
			}

			return type ?? new UnknownType(fullTypeName, isReferenceType);
		}

		internal static IType ResolveInAllAssemblies(ICompilation compilation, in FullTypeName fullTypeName)
		{
			foreach (var asm in compilation.Modules)
			{
				IType type = asm.GetTypeDefinition(fullTypeName);
				if (type != null)
					return type;
			}

			return null;
		}

		public override string ToString()
		{
			return fullTypeName + (Module != null ? ", " + Module : null);
		}

		public int GetHashCodeForInterning()
		{
			unchecked
			{
				return 33 * Module.GetHashCode() + fullTypeName.GetHashCode();
			}
		}

		public bool EqualsForInterning(ISupportsInterning other)
		{
			return other is GetClassTypeReference o && Module == o.Module && fullTypeName == o.fullTypeName &&
			       isReferenceType == o.isReferenceType;
		}
	}
}