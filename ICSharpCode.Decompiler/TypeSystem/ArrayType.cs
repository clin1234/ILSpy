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

using ICSharpCode.Decompiler.TypeSystem.Implementation;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Represents an array type.
	/// </summary>
	public sealed class ArrayType : TypeWithElementType, ICompilationProvider
	{
		internal ArrayType(ICompilation compilation, IType elementType, int dimensions = 1,
			Nullability nullability = Nullability.Oblivious) : base(elementType)
		{
			if (dimensions <= 0)
				throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "dimensions must be positive");
			this.Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
			this.Dimensions = dimensions;
			this.Nullability = nullability;

			if (elementType is ICompilationProvider p && p.Compilation != compilation)
				throw new InvalidOperationException(
					"Cannot create an array type using a different compilation from the element type.");
		}

		public override TypeKind Kind {
			get { return TypeKind.Array; }
		}

		internal int Dimensions { get; }

		public override Nullability Nullability { get; }

		protected override string NameSuffix {
			get {
				return "[" + new string(',', Dimensions - 1) + "]";
			}
		}

		public override bool? IsReferenceType {
			get { return true; }
		}

		public override IEnumerable<IType> DirectBaseTypes {
			get {
				List<IType> baseTypes = new();
				IType t = Compilation.FindType(KnownTypeCode.Array);
				if (t.Kind != TypeKind.Unknown)
					baseTypes.Add(t);
				if (Dimensions == 1 && elementType.Kind != TypeKind.Pointer)
				{
					// single-dimensional arrays implement IList<T>
					if (Compilation.FindType(KnownTypeCode.IListOfT) is ITypeDefinition def)
						baseTypes.Add(new ParameterizedType(def, new[] { elementType }));
					// And in .NET 4.5 they also implement IReadOnlyList<T>
					def = Compilation.FindType(KnownTypeCode.IReadOnlyListOfT) as ITypeDefinition;
					if (def != null)
						baseTypes.Add(new ParameterizedType(def, new[] { elementType }));
				}

				return baseTypes;
			}
		}

		public ICompilation Compilation { get; }

		public override IType ChangeNullability(Nullability nullability)
		{
			if (nullability == this.Nullability)
				return this;
			return new ArrayType(Compilation, elementType, Dimensions, nullability);
		}

		public override int GetHashCode()
		{
			return unchecked(elementType.GetHashCode() * 71681 + Dimensions);
		}

		public override bool Equals(IType other)
		{
			return other is ArrayType a && elementType.Equals(a.elementType) && a.Dimensions == Dimensions &&
			       a.Nullability == Nullability;
		}

		public override string ToString()
		{
			return Nullability switch {
				Nullability.Nullable => elementType + NameSuffix + "?",
				Nullability.NotNullable => elementType + NameSuffix + "!",
				_ => elementType + NameSuffix
			};
		}

		public override IEnumerable<IMethod> GetMethods(Predicate<IMethod> filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.IgnoreInheritedMembers) == GetMemberOptions.IgnoreInheritedMembers)
				return EmptyList<IMethod>.Instance;
			return Compilation.FindType(KnownTypeCode.Array).GetMethods(filter, options);
		}

		public override IEnumerable<IMethod> GetMethods(IReadOnlyList<IType> typeArguments,
			Predicate<IMethod> filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.IgnoreInheritedMembers) == GetMemberOptions.IgnoreInheritedMembers)
				return EmptyList<IMethod>.Instance;
			return Compilation.FindType(KnownTypeCode.Array).GetMethods(typeArguments, filter, options);
		}

		public override IEnumerable<IMethod> GetAccessors(Predicate<IMethod> filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.IgnoreInheritedMembers) == GetMemberOptions.IgnoreInheritedMembers)
				return EmptyList<IMethod>.Instance;
			return Compilation.FindType(KnownTypeCode.Array).GetAccessors(filter, options);
		}

		public override IEnumerable<IProperty> GetProperties(Predicate<IProperty> filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.IgnoreInheritedMembers) == GetMemberOptions.IgnoreInheritedMembers)
				return EmptyList<IProperty>.Instance;
			return Compilation.FindType(KnownTypeCode.Array).GetProperties(filter, options);
		}

		// NestedTypes, Events, Fields: System.Array doesn't have any; so we can use the AbstractType default implementation
		// that simply returns an empty list

		public override IType AcceptVisitor(TypeVisitor visitor)
		{
			return visitor.VisitArrayType(this);
		}

		public override IType VisitChildren(TypeVisitor visitor)
		{
			IType e = elementType.AcceptVisitor(visitor);
			return e == elementType ? this : new ArrayType(Compilation, e, Dimensions, Nullability);
		}
	}

	[Serializable]
	public sealed class ArrayTypeReference : ITypeReference, ISupportsInterning
	{
		public ArrayTypeReference(ITypeReference elementType, int dimensions = 1)
		{
			if (dimensions <= 0)
				throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "dimensions must be positive");
			this.ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
			this.Dimensions = dimensions;
		}

		public ITypeReference ElementType { get; }

		public int Dimensions { get; }

		public IType Resolve(ITypeResolveContext context)
		{
			return new ArrayType(context.Compilation, ElementType.Resolve(context), Dimensions);
		}

		public override string ToString()
		{
			return ElementType + "[" + new string(',', Dimensions - 1) + "]";
		}

		public int GetHashCodeForInterning()
		{
			return ElementType.GetHashCode() ^ Dimensions;
		}

		public bool EqualsForInterning(ISupportsInterning other)
		{
			return other is ArrayTypeReference o && ElementType == o.ElementType && Dimensions == o.Dimensions;
		}
	}
}