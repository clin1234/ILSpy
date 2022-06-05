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

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Static helper methods for working with nullable types.
	/// </summary>
	internal static class NullableType
	{
		/// <summary>
		/// Gets whether the specified type is a nullable type.
		/// </summary>
		internal static bool IsNullable(IType type)
		{
			ArgumentNullException.ThrowIfNull(type);
			return type.SkipModifiers() is ParameterizedType { TypeParameterCount: 1 } pt &&
			       pt.GenericType.IsKnownType(KnownTypeCode.NullableOfT);
		}

		internal static bool IsNonNullableValueType(IType type)
		{
			return type.IsReferenceType == false && !IsNullable(type);
		}

		/// <summary>
		/// Returns the element type, if <paramref name="type"/> is a nullable type.
		/// Otherwise, returns the type itself.
		/// </summary>
		internal static IType GetUnderlyingType(IType type)
		{
			ArgumentNullException.ThrowIfNull(type);
			if (type.SkipModifiers() is ParameterizedType { TypeParameterCount: 1 } pt &&
			    pt.GenericType.IsKnownType(KnownTypeCode.NullableOfT))
				return pt.GetTypeArgument(0);
			return type;
		}

		/// <summary>
		/// Creates a nullable type.
		/// </summary>
		internal static IType Create(ICompilation compilation, IType elementType)
		{
			ArgumentNullException.ThrowIfNull(compilation);
			ArgumentNullException.ThrowIfNull(elementType);

			IType nullableType = compilation.FindType(KnownTypeCode.NullableOfT);
			ITypeDefinition nullableTypeDef = nullableType.GetDefinition();
			if (nullableTypeDef != null)
				return new ParameterizedType(nullableTypeDef, new[] { elementType });
			return nullableType;
		}

		/// <summary>
		/// Creates a nullable type reference.
		/// </summary>
		internal static ParameterizedTypeReference Create(ITypeReference elementType)
		{
			ArgumentNullException.ThrowIfNull(elementType);
			return new ParameterizedTypeReference(KnownTypeReference.Get(KnownTypeCode.NullableOfT),
				new[] { elementType });
		}
	}
}