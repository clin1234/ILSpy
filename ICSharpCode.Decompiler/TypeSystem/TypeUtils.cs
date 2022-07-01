// Copyright (c) 2015 Siegfried Pammer
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

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	public static class TypeUtils
	{
		public const int NativeIntSize = 6; // between 4 (Int32) and 8 (Int64)

		/// <summary>
		/// Gets the size (in bytes) of the input type.
		/// Returns <c>NativeIntSize</c> for pointer-sized types.
		/// Returns 0 for structs and other types of unknown size.
		/// </summary>
		public static int GetSize(this IType type)
		{
			switch (type.Kind)
			{
				case TypeKind.Pointer:
				case TypeKind.ByReference:
				case TypeKind.Class:
				case TypeKind.NInt:
				case TypeKind.NUInt:
					return NativeIntSize;
				case TypeKind.Enum:
					type = type.GetEnumUnderlyingType();
					break;
				case TypeKind.ModOpt:
				case TypeKind.ModReq:
					return type.SkipModifiers().GetSize();
			}

			var typeDef = type.GetDefinition();
			if (typeDef == null)
				return 0;
			return typeDef.KnownTypeCode switch {
				KnownTypeCode.Boolean or KnownTypeCode.SByte or KnownTypeCode.Byte => 1,
				KnownTypeCode.Char or KnownTypeCode.Int16 or KnownTypeCode.UInt16 => 2,
				KnownTypeCode.Int32 or KnownTypeCode.UInt32 or KnownTypeCode.Single => 4,
				KnownTypeCode.IntPtr or KnownTypeCode.UIntPtr => NativeIntSize,
				KnownTypeCode.Int64 or KnownTypeCode.UInt64 or KnownTypeCode.Double => 8,
				_ => 0,
			};
		}

		/// <summary>
		/// Gets the size of the input stack type.
		/// </summary>
		/// <returns>
		/// * 4 for <c>I4</c>,
		/// * 8 for <c>I8</c>,
		/// * <c>NativeIntSize</c> for <c>I</c> and <c>Ref</c>,
		/// * 0 otherwise (O, F, Void, Unknown).
		/// </returns>
		public static int GetSize(this StackType type)
		{
			return type switch {
				StackType.I4 => 4,
				StackType.I8 => 8,
				StackType.I or StackType.Ref => NativeIntSize,
				_ => 0,
			};
		}

		public static IType GetLargerType(IType type1, IType type2)
		{
			return GetSize(type1) >= GetSize(type2) ? type1 : type2;
		}

		/// <summary>
		/// Gets whether the type is a small integer type.
		/// Small integer types are:
		/// * bool, sbyte, byte, char, short, ushort
		/// * any enums that have a small integer type as underlying type
		/// </summary>
		public static bool IsSmallIntegerType(this IType type)
		{
			int size = GetSize(type);
			return size is > 0 and < 4;
		}

		/// <summary>
		/// Gets whether the type is a C# small integer type: byte, sbyte, short or ushort.
		/// 
		/// Unlike the ILAst, C# does not consider bool, char or enums to be small integers.
		/// </summary>
		public static bool IsCSharpSmallIntegerType(this IType type)
		{
			return (type.GetDefinition()?.KnownTypeCode) switch {
				KnownTypeCode.Byte or KnownTypeCode.SByte or KnownTypeCode.Int16 or KnownTypeCode.UInt16 => true,
				_ => false,
			};
		}

		/// <summary>
		/// Gets whether the type is a C# 9 native integer type: nint or nuint.
		/// 
		/// Returns false for (U)IntPtr.
		/// </summary>
		public static bool IsCSharpNativeIntegerType(this IType type)
		{
			return type.Kind switch {
				TypeKind.NInt or TypeKind.NUInt => true,
				_ => false,
			};
		}

		/// <summary>
		/// Gets whether the type is a C# primitive integer type: byte, sbyte, short, ushort, int, uint, long and ulong.
		/// 
		/// Unlike the ILAst, C# does not consider bool, enums, pointers or IntPtr to be integers.
		/// </summary>
		public static bool IsCSharpPrimitiveIntegerType(this IType type)
		{
			return (type.GetDefinition()?.KnownTypeCode) switch {
				KnownTypeCode.Byte or KnownTypeCode.SByte or KnownTypeCode.Int16 or KnownTypeCode.UInt16 or KnownTypeCode.Int32 or KnownTypeCode.UInt32 or KnownTypeCode.Int64 or KnownTypeCode.UInt64 => true,
				_ => false,
			};
		}

		/// <summary>
		/// Gets whether the type is an IL integer type.
		/// Returns true for I4, I, or I8.
		/// </summary>
		public static bool IsIntegerType(this StackType type)
		{
			return type switch {
				StackType.I4 or StackType.I or StackType.I8 => true,
				_ => false,
			};
		}

		/// <summary>
		/// Gets whether the type is an IL floating point type.
		/// Returns true for F4 or F8.
		/// </summary>
		public static bool IsFloatType(this StackType type)
		{
			return type switch {
				StackType.F4 or StackType.F8 => true,
				_ => false,
			};
		}

		/// <summary>
		/// Gets whether reading/writing an element of accessType from the pointer
		/// is equivalent to reading/writing an element of the pointer's element type.
		/// </summary>
		/// <remarks>
		/// The access semantics may sligthly differ on read accesses of small integer types,
		/// due to zero extension vs. sign extension when the signs differ.
		/// </remarks>
		public static bool IsCompatiblePointerTypeForMemoryAccess(IType pointerType, IType accessType)
		{
			IType memoryType;
			if (pointerType is PointerType or ByReferenceType)
				memoryType = ((TypeWithElementType)pointerType).ElementType;
			else
				return false;
			return IsCompatibleTypeForMemoryAccess(memoryType, accessType);
		}

		/// <summary>
		/// Gets whether reading/writing an element of accessType from the pointer
		/// is equivalent to reading/writing an element of the memoryType.
		/// </summary>
		/// <remarks>
		/// The access semantics may sligthly differ on read accesses of small integer types,
		/// due to zero extension vs. sign extension when the signs differ.
		/// </remarks>
		public static bool IsCompatibleTypeForMemoryAccess(IType memoryType, IType accessType)
		{
			memoryType = memoryType.AcceptVisitor(NormalizeTypeVisitor.TypeErasure);
			accessType = accessType.AcceptVisitor(NormalizeTypeVisitor.TypeErasure);
			if (memoryType.Equals(accessType))
				return true;
			// If the types are not equal, the access still might produce equal results in some cases:
			// 1) Both types are reference types
			if (memoryType.IsReferenceType == true && accessType.IsReferenceType == true)
				return true;
			// 2) Both types are integer types of equal size
			StackType memoryStackType = memoryType.GetStackType();
			StackType accessStackType = accessType.GetStackType();
			if (memoryStackType == accessStackType && memoryStackType.IsIntegerType() && GetSize(memoryType) == GetSize(accessType))
				return true;
			// 3) Any of the types is unknown: we assume they are compatible.
			return memoryType.Kind == TypeKind.Unknown || accessType.Kind == TypeKind.Unknown;
		}

		/// <summary>
		/// Gets the stack type corresponding to this type.
		/// </summary>
		public static StackType GetStackType(this IType type)
		{
			switch (type.Kind)
			{
				case TypeKind.Unknown:
					if (type.IsReferenceType == true)
					{
						return StackType.O;
					}
					return StackType.Unknown;
				case TypeKind.ByReference:
					return StackType.Ref;
				case TypeKind.Pointer:
				case TypeKind.NInt:
				case TypeKind.NUInt:
				case TypeKind.FunctionPointer:
					return StackType.I;
				case TypeKind.TypeParameter:
					// Type parameters are always considered StackType.O, even
					// though they might be instantiated with primitive types.
					return StackType.O;
				case TypeKind.ModOpt:
				case TypeKind.ModReq:
					return type.SkipModifiers().GetStackType();
			}
			ITypeDefinition typeDef = type.GetEnumUnderlyingType().GetDefinition();
			if (typeDef == null)
				return StackType.O;
			return typeDef.KnownTypeCode switch {
				KnownTypeCode.Boolean or KnownTypeCode.Char or KnownTypeCode.SByte or KnownTypeCode.Byte or KnownTypeCode.Int16 or KnownTypeCode.UInt16 or KnownTypeCode.Int32 or KnownTypeCode.UInt32 => StackType.I4,
				KnownTypeCode.Int64 or KnownTypeCode.UInt64 => StackType.I8,
				KnownTypeCode.Single => StackType.F4,
				KnownTypeCode.Double => StackType.F8,
				KnownTypeCode.Void => StackType.Void,
				KnownTypeCode.IntPtr or KnownTypeCode.UIntPtr => StackType.I,
				_ => StackType.O,
			};
		}

		/// <summary>
		/// If type is an enumeration type, returns the underlying type.
		/// Otherwise, returns type unmodified.
		/// </summary>
		public static IType GetEnumUnderlyingType(this IType type)
		{
			type = type.SkipModifiers();
			return (type.Kind == TypeKind.Enum) ? type.GetDefinition().EnumUnderlyingType : type;
		}

		/// <summary>
		/// Gets the sign of the input type.
		/// </summary>
		/// <remarks>
		/// Integer types (including IntPtr/UIntPtr) return the sign as expected.
		/// Floating point types and <c>decimal</c> are considered to be signed.
		/// <c>char</c>, <c>bool</c> and pointer types (e.g. <c>void*</c>) are unsigned.
		/// Enums have a sign based on their underlying type.
		/// All other types return <c>Sign.None</c>.
		/// </remarks>
		public static Sign GetSign(this IType type)
		{
			type = type.SkipModifiers();
			switch (type.Kind)
			{
				case TypeKind.Pointer:
				case TypeKind.NUInt:
				case TypeKind.FunctionPointer:
					return Sign.Unsigned;
				case TypeKind.NInt:
					return Sign.Signed;
			}
			var typeDef = type.GetEnumUnderlyingType().GetDefinition();
			if (typeDef == null)
				return Sign.None;
			return typeDef.KnownTypeCode switch {
				KnownTypeCode.SByte or KnownTypeCode.Int16 or KnownTypeCode.Int32 or KnownTypeCode.Int64 or KnownTypeCode.IntPtr or KnownTypeCode.Single or KnownTypeCode.Double or KnownTypeCode.Decimal => Sign.Signed,
				KnownTypeCode.UIntPtr or KnownTypeCode.Char or KnownTypeCode.Boolean or KnownTypeCode.Byte or KnownTypeCode.UInt16 or KnownTypeCode.UInt32 or KnownTypeCode.UInt64 => Sign.Unsigned,
				_ => Sign.None,
			};
		}

		/// <summary>
		/// Maps the KnownTypeCode values to the corresponding PrimitiveTypes.
		/// </summary>
		public static PrimitiveType ToPrimitiveType(this KnownTypeCode knownTypeCode)
		{
			return knownTypeCode switch {
				KnownTypeCode.SByte => PrimitiveType.I1,
				KnownTypeCode.Int16 => PrimitiveType.I2,
				KnownTypeCode.Int32 => PrimitiveType.I4,
				KnownTypeCode.Int64 => PrimitiveType.I8,
				KnownTypeCode.Single => PrimitiveType.R4,
				KnownTypeCode.Double => PrimitiveType.R8,
				KnownTypeCode.Byte => PrimitiveType.U1,
				KnownTypeCode.UInt16 or KnownTypeCode.Char => PrimitiveType.U2,
				KnownTypeCode.UInt32 => PrimitiveType.U4,
				KnownTypeCode.UInt64 => PrimitiveType.U8,
				KnownTypeCode.IntPtr => PrimitiveType.I,
				KnownTypeCode.UIntPtr => PrimitiveType.U,
				_ => PrimitiveType.None,
			};
		}

		/// <summary>
		/// Maps the KnownTypeCode values to the corresponding PrimitiveTypes.
		/// </summary>
		public static PrimitiveType ToPrimitiveType(this IType type)
		{
			type = type.SkipModifiers();
			switch (type.Kind)
			{
				case TypeKind.Unknown:
					return PrimitiveType.Unknown;
				case TypeKind.ByReference:
					return PrimitiveType.Ref;
				case TypeKind.NInt:
				case TypeKind.FunctionPointer:
					return PrimitiveType.I;
				case TypeKind.NUInt:
					return PrimitiveType.U;
			}
			var def = type.GetEnumUnderlyingType().GetDefinition();
			return def != null ? def.KnownTypeCode.ToPrimitiveType() : PrimitiveType.None;
		}

		/// <summary>
		/// Maps the PrimitiveType values to the corresponding KnownTypeCodes.
		/// </summary>
		public static KnownTypeCode ToKnownTypeCode(this PrimitiveType primitiveType)
		{
			return primitiveType switch {
				PrimitiveType.I1 => KnownTypeCode.SByte,
				PrimitiveType.I2 => KnownTypeCode.Int16,
				PrimitiveType.I4 => KnownTypeCode.Int32,
				PrimitiveType.I8 => KnownTypeCode.Int64,
				PrimitiveType.R4 => KnownTypeCode.Single,
				PrimitiveType.R8 or PrimitiveType.R => KnownTypeCode.Double,
				PrimitiveType.U1 => KnownTypeCode.Byte,
				PrimitiveType.U2 => KnownTypeCode.UInt16,
				PrimitiveType.U4 => KnownTypeCode.UInt32,
				PrimitiveType.U8 => KnownTypeCode.UInt64,
				PrimitiveType.I => KnownTypeCode.IntPtr,
				PrimitiveType.U => KnownTypeCode.UIntPtr,
				_ => KnownTypeCode.None,
			};
		}

		public static KnownTypeCode ToKnownTypeCode(this StackType stackType, Sign sign = Sign.None)
		{
			return stackType switch {
				StackType.I4 => sign == Sign.Unsigned ? KnownTypeCode.UInt32 : KnownTypeCode.Int32,
				StackType.I8 => sign == Sign.Unsigned ? KnownTypeCode.UInt64 : KnownTypeCode.Int64,
				StackType.I => sign == Sign.Unsigned ? KnownTypeCode.UIntPtr : KnownTypeCode.IntPtr,
				StackType.F4 => KnownTypeCode.Single,
				StackType.F8 => KnownTypeCode.Double,
				StackType.O => KnownTypeCode.Object,
				StackType.Void => KnownTypeCode.Void,
				_ => KnownTypeCode.None
			};
		}

		public static PrimitiveType ToPrimitiveType(this StackType stackType, Sign sign = Sign.None)
		{
			return stackType switch {
				StackType.I4 => sign == Sign.Unsigned ? PrimitiveType.U4 : PrimitiveType.I4,
				StackType.I8 => sign == Sign.Unsigned ? PrimitiveType.U8 : PrimitiveType.I8,
				StackType.I => sign == Sign.Unsigned ? PrimitiveType.U : PrimitiveType.I,
				StackType.F4 => PrimitiveType.R4,
				StackType.F8 => PrimitiveType.R8,
				StackType.Ref => PrimitiveType.Ref,
				StackType.Unknown => PrimitiveType.Unknown,
				_ => PrimitiveType.None
			};
		}
	}

	public enum Sign : byte
	{
		None,
		Signed,
		Unsigned
	}
}
