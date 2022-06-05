using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	sealed class NormalizeTypeVisitor : TypeVisitor
	{
		/// <summary>
		/// NormalizeTypeVisitor that does not normalize type parameters,
		/// but performs type erasure (object->dynamic; tuple->underlying type).
		/// </summary>
		internal static readonly NormalizeTypeVisitor TypeErasure = new() {
			ReplaceClassTypeParametersWithDummy = false,
			ReplaceMethodTypeParametersWithDummy = false,
			DynamicAndObject = true,
			IntPtrToNInt = true,
			TupleToUnderlyingType = true,
			RemoveModOpt = true,
			RemoveModReq = true,
			RemoveNullability = true,
		};

		internal static readonly NormalizeTypeVisitor IgnoreNullabilityAndTuples = new() {
			ReplaceClassTypeParametersWithDummy = false,
			ReplaceMethodTypeParametersWithDummy = false,
			DynamicAndObject = false,
			IntPtrToNInt = false,
			TupleToUnderlyingType = true,
			RemoveModOpt = true,
			RemoveModReq = true,
			RemoveNullability = true,
		};

		public bool DynamicAndObject = true;
		public bool IntPtrToNInt = true;

		public bool RemoveModOpt = true;
		public bool RemoveModReq = true;
		public bool RemoveNullability = true;
		public bool ReplaceClassTypeParametersWithDummy = true;
		public bool ReplaceMethodTypeParametersWithDummy = true;
		public bool TupleToUnderlyingType = true;

		public bool EquivalentTypes(IType a, IType b)
		{
			a = a.AcceptVisitor(this);
			b = b.AcceptVisitor(this);
			return a.Equals(b);
		}

		internal override IType VisitTypeParameter(ITypeParameter type)
		{
			switch (type.OwnerType)
			{
				case SymbolKind.Method when ReplaceMethodTypeParametersWithDummy:
					return DummyTypeParameter.GetMethodTypeParameter(type.Index);
				case SymbolKind.TypeDefinition when ReplaceClassTypeParametersWithDummy:
					return DummyTypeParameter.GetClassTypeParameter(type.Index);
				default:
				{
					if (RemoveNullability && type is NullabilityAnnotatedTypeParameter natp)
					{
						return natp.TypeWithoutAnnotation.AcceptVisitor(this);
					}

					return base.VisitTypeParameter(type);
				}
			}
		}

		internal override IType VisitTypeDefinition(ITypeDefinition type)
		{
			switch (type.KnownTypeCode)
			{
				case KnownTypeCode.Object when DynamicAndObject:
					// Instead of normalizing dynamic->object,
					// we do this the opposite direction, so that we don't need a compilation to find the object type.
					if (RemoveNullability)
						return SpecialType.Dynamic;
					return SpecialType.Dynamic.ChangeNullability(type.Nullability);
				case KnownTypeCode.IntPtr when IntPtrToNInt:
					return SpecialType.NInt;
				case KnownTypeCode.UIntPtr when IntPtrToNInt:
					return SpecialType.NUInt;
			}

			return base.VisitTypeDefinition(type);
		}

		internal override IType VisitTupleType(TupleType type)
		{
			if (TupleToUnderlyingType)
			{
				return type.UnderlyingType.AcceptVisitor(this);
			}

			return base.VisitTupleType(type);
		}

		internal override IType VisitNullabilityAnnotatedType(NullabilityAnnotatedType type)
		{
			if (RemoveNullability)
				return type.TypeWithoutAnnotation.AcceptVisitor(this);
			return base.VisitNullabilityAnnotatedType(type);
		}

		internal override IType VisitArrayType(ArrayType type)
		{
			if (RemoveNullability)
				return base.VisitArrayType(type).ChangeNullability(Nullability.Oblivious);
			return base.VisitArrayType(type);
		}

		internal override IType VisitModOpt(ModifiedType type)
		{
			if (RemoveModOpt)
			{
				return type.ElementType.AcceptVisitor(this);
			}

			return base.VisitModOpt(type);
		}

		internal override IType VisitModReq(ModifiedType type)
		{
			if (RemoveModReq)
			{
				return type.ElementType.AcceptVisitor(this);
			}

			return base.VisitModReq(type);
		}
	}
}