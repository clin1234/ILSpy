using System.Collections.Generic;
using System.Diagnostics;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// A decorator that annotates the nullability status for a type.
	/// Note: ArrayType does not use a decorator, but has direct support for nullability.
	/// </summary>
	public class NullabilityAnnotatedType : DecoratedType, IType
	{
		internal NullabilityAnnotatedType(IType type, Nullability nullability)
			: base(type)
		{
			Debug.Assert(type.Nullability == Nullability.Oblivious);
			Debug.Assert(nullability != Nullability.Oblivious);
			// Due to IType -> concrete type casts all over the type system, we can insert
			// the NullabilityAnnotatedType wrapper only in some limited places.
			Debug.Assert(type is ITypeDefinition
			             || type.Kind is TypeKind.Dynamic or TypeKind.Unknown ||
			             (type is ITypeParameter && this is ITypeParameter));
			this.Nullability = nullability;
		}

		internal IType TypeWithoutAnnotation => baseType;

		public Nullability Nullability { get; }

		public override IType AcceptVisitor(TypeVisitor visitor)
		{
			return visitor.VisitNullabilityAnnotatedType(this);
		}

		public override bool Equals(IType other)
		{
			return other is NullabilityAnnotatedType nat
			       && nat.Nullability == Nullability
			       && nat.baseType.Equals(baseType);
		}

		public override IType ChangeNullability(Nullability nullability)
		{
			if (nullability == this.Nullability)
				return this;
			return baseType.ChangeNullability(nullability);
		}

		public override IType VisitChildren(TypeVisitor visitor)
		{
			IType newBase = baseType.AcceptVisitor(visitor);
			if (newBase != baseType)
			{
				if (newBase.Nullability == Nullability.Nullable)
				{
					// `T!` with substitution T=`U?` becomes `U?`
					// This happens during type substitution for generic methods.
					return newBase;
				}

				if (newBase.Kind == TypeKind.TypeParameter || newBase.IsReferenceType == true)
				{
					return newBase.ChangeNullability(Nullability);
				}

				// `T!` with substitution T=`int` becomes `int`, not `int!`
				return newBase;
			}

			return this;
		}

		public override string ToString()
		{
			switch (Nullability)
			{
				case Nullability.Nullable:
					return $"{baseType}?";
				case Nullability.NotNullable:
					return $"{baseType}!";
				default:
					Debug.Assert(Nullability == Nullability.Oblivious);
					return $"{baseType}~";
			}
		}
	}

	internal sealed class NullabilityAnnotatedTypeParameter : NullabilityAnnotatedType, ITypeParameter
	{
		internal NullabilityAnnotatedTypeParameter(ITypeParameter type, Nullability nullability)
			: base(type, nullability)
		{
			this.OriginalTypeParameter = type;
		}

		public ITypeParameter OriginalTypeParameter { get; }

		SymbolKind ITypeParameter.OwnerType => OriginalTypeParameter.OwnerType;
		IEntity ITypeParameter.Owner => OriginalTypeParameter.Owner;
		int ITypeParameter.Index => OriginalTypeParameter.Index;
		string ITypeParameter.Name => OriginalTypeParameter.Name;
		string ISymbol.Name => OriginalTypeParameter.Name;
		VarianceModifier ITypeParameter.Variance => OriginalTypeParameter.Variance;
		IType ITypeParameter.EffectiveBaseClass => OriginalTypeParameter.EffectiveBaseClass;
		IReadOnlyCollection<IType> ITypeParameter.EffectiveInterfaceSet => OriginalTypeParameter.EffectiveInterfaceSet;
		bool ITypeParameter.HasDefaultConstructorConstraint => OriginalTypeParameter.HasDefaultConstructorConstraint;
		bool ITypeParameter.HasReferenceTypeConstraint => OriginalTypeParameter.HasReferenceTypeConstraint;
		bool ITypeParameter.HasValueTypeConstraint => OriginalTypeParameter.HasValueTypeConstraint;
		bool ITypeParameter.HasUnmanagedConstraint => OriginalTypeParameter.HasUnmanagedConstraint;
		Nullability ITypeParameter.NullabilityConstraint => OriginalTypeParameter.NullabilityConstraint;
		IReadOnlyList<TypeConstraint> ITypeParameter.TypeConstraints => OriginalTypeParameter.TypeConstraints;
		SymbolKind ISymbol.SymbolKind => SymbolKind.TypeParameter;
		IEnumerable<IAttribute> ITypeParameter.GetAttributes() => OriginalTypeParameter.GetAttributes();
	}
}