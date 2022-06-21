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

using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Contains static implementations of special types.
	/// </summary>
	[Serializable]
	public sealed class SpecialType : AbstractType, ITypeReference
	{
		/// <summary>
		/// Gets the type representing resolve errors.
		/// </summary>
		public static readonly SpecialType UnknownType = new(TypeKind.Unknown, "?", isReferenceType: null);

		/// <summary>
		/// The null type is used as type of the null literal. It is a reference type without any members; and it is a subtype of all reference types.
		/// </summary>
		public static readonly SpecialType NullType = new(TypeKind.Null, "null", isReferenceType: true);

		/// <summary>
		/// Used for expressions without type, e.g. method groups or lambdas.
		/// </summary>
		public static readonly SpecialType NoType = new(TypeKind.None, "?", isReferenceType: null);

		/// <summary>
		/// Type representing the C# 'dynamic' type.
		/// </summary>
		public static readonly SpecialType Dynamic = new(TypeKind.Dynamic, "dynamic", isReferenceType: true);

		/// <summary>
		/// Type representing the C# 9 'nint' type.
		/// </summary>
		public static readonly SpecialType NInt = new(TypeKind.NInt, "nint", isReferenceType: false);

		/// <summary>
		/// Type representing the C# 9 'nuint' type.
		/// </summary>
		public static readonly SpecialType NUInt = new(TypeKind.NUInt, "nuint", isReferenceType: false);

		/// <summary>
		/// Type representing the result of the C# '__arglist()' expression.
		/// </summary>
		public static readonly SpecialType ArgList = new(TypeKind.ArgList, "__arglist", isReferenceType: null);

		/// <summary>
		/// A type used for unbound type arguments in partially parameterized types.
		/// </summary>
		/// <see cref="IType.GetNestedTypes(System.Predicate{ICSharpCode.Decompiler.TypeSystem.ITypeDefinition}(ICSharpCode.Decompiler.TypeSystem.ITypeDefinition), GetMemberOptions)"/>
		public static readonly SpecialType UnboundTypeArgument =
			new(TypeKind.UnboundTypeArgument, "", isReferenceType: null);

		private SpecialType(TypeKind kind, string name, bool? isReferenceType)
		{
			this.Kind = kind;
			this.Name = name;
			this.IsReferenceType = isReferenceType;
		}

		public override string Name { get; }

		public override TypeKind Kind { get; }

		public override bool? IsReferenceType { get; }

		IType ITypeReference.Resolve(ITypeResolveContext context)
		{
			if (context is null) throw new ArgumentNullException(nameof(context));
			return this;
		}
#pragma warning disable 809
		[Obsolete("Please compare special types using the kind property instead.")]
		public override bool Equals(IType other)
		{
			// We consider a special types equal when they have equal types.
			// However, an unknown type with additional information is not considered to be equal to the SpecialType with TypeKind.Unknown.
			return other is SpecialType && other.Kind == Kind;
		}

		public override int GetHashCode()
		{
			return 81625621 ^ (int)Kind;
		}

		public override IType ChangeNullability(Nullability nullability)
		{
			if (nullability == base.Nullability)
				return this;
			return new NullabilityAnnotatedType(this, nullability);
		}
	}
}