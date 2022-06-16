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
using System.Globalization;

using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	[Serializable]
	public sealed class TypeParameterReference : ITypeReference
	{
		static readonly TypeParameterReference?[] classTypeParameterReferences = new TypeParameterReference?[8];
		static readonly TypeParameterReference?[] methodTypeParameterReferences = new TypeParameterReference?[8];

		readonly SymbolKind ownerType;

		public TypeParameterReference(SymbolKind ownerType, int index)
		{
			this.ownerType = ownerType;
			this.Index = index;
		}

		public int Index { get; }

		public IType Resolve(ITypeResolveContext context)
		{
			switch (ownerType)
			{
				case SymbolKind.Method
					when context.CurrentMember is IMethod method && Index < method.TypeParameters.Count:
					return method.TypeParameters[Index];
				case SymbolKind.Method:
					return DummyTypeParameter.GetMethodTypeParameter(Index);
				case SymbolKind.TypeDefinition:
				{
					ITypeDefinition typeDef = context.CurrentTypeDefinition;
					if (typeDef != null && Index < typeDef.TypeParameters.Count)
					{
						return typeDef.TypeParameters[Index];
					}

					return DummyTypeParameter.GetClassTypeParameter(Index);
				}
				default:
					return SpecialType.UnknownType;
			}
		}

		/// <summary>
		/// Creates a type parameter reference.
		/// For common type parameter references, this method may return a shared instance.
		/// </summary>
		public static TypeParameterReference Create(SymbolKind ownerType, int index)
		{
			if (index is >= 0 and < 8 && ownerType is SymbolKind.TypeDefinition or SymbolKind.Method)
			{
				TypeParameterReference?[] arr = (ownerType == SymbolKind.TypeDefinition)
					? classTypeParameterReferences
					: methodTypeParameterReferences;
				TypeParameterReference? result = LazyInit.VolatileRead(ref arr[index]) ??
				                                 LazyInit.GetOrSet(ref arr[index],
					                                 new TypeParameterReference(ownerType, index));
				return result;
			}

			return new TypeParameterReference(ownerType, index);
		}

		public override string ToString()
		{
			if (ownerType == SymbolKind.Method)
				return "!!" + Index.ToString(CultureInfo.InvariantCulture);
			return "!" + Index.ToString(CultureInfo.InvariantCulture);
		}
	}
}