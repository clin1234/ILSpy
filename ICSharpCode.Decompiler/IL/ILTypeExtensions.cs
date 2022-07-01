#nullable enable
// Copyright (c) 2014 Daniel Grunwald
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

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.IL
{
	static class ILTypeExtensions
	{
		public static StackType GetStackType(this PrimitiveType primitiveType)
		{
			return primitiveType switch {
				PrimitiveType.I1 or PrimitiveType.U1 or PrimitiveType.I2 or PrimitiveType.U2 or PrimitiveType.I4 or PrimitiveType.U4 => StackType.I4,
				PrimitiveType.I8 or PrimitiveType.U8 => StackType.I8,
				PrimitiveType.I or PrimitiveType.U => StackType.I,
				PrimitiveType.R4 => StackType.F4,
				PrimitiveType.R8 or PrimitiveType.R => StackType.F8,
				// ByRef
				PrimitiveType.Ref => StackType.Ref,
				PrimitiveType.Unknown => StackType.Unknown,
				_ => StackType.O,
			};
		}

		public static Sign GetSign(this PrimitiveType primitiveType)
		{
			return primitiveType switch {
				PrimitiveType.I1 or PrimitiveType.I2 or PrimitiveType.I4 or PrimitiveType.I8 or PrimitiveType.R4 or PrimitiveType.R8 or PrimitiveType.R or PrimitiveType.I => Sign.Signed,
				PrimitiveType.U1 or PrimitiveType.U2 or PrimitiveType.U4 or PrimitiveType.U8 or PrimitiveType.U => Sign.Unsigned,
				_ => Sign.None,
			};
		}

		/// <summary>
		/// Gets the size in bytes of the primitive type.
		/// 
		/// Returns 0 for non-primitive types.
		/// Returns <c>NativeIntSize</c> for native int/references.
		/// </summary>
		public static int GetSize(this PrimitiveType type)
		{
			return type switch {
				PrimitiveType.I1 or PrimitiveType.U1 => 1,
				PrimitiveType.I2 or PrimitiveType.U2 => 2,
				PrimitiveType.I4 or PrimitiveType.U4 or PrimitiveType.R4 => 4,
				PrimitiveType.I8 or PrimitiveType.R8 or PrimitiveType.U8 or PrimitiveType.R => 8,
				PrimitiveType.I or PrimitiveType.U or PrimitiveType.Ref => TypeUtils.NativeIntSize,
				_ => 0,
			};
		}

		/// <summary>
		/// Gets whether the type is a small integer type.
		/// Small integer types are:
		/// * bool, sbyte, byte, char, short, ushort
		/// * any enums that have a small integer type as underlying type
		/// </summary>
		public static bool IsSmallIntegerType(this PrimitiveType type)
		{
			return GetSize(type) < 4;
		}

		public static bool IsIntegerType(this PrimitiveType primitiveType)
		{
			return primitiveType.GetStackType().IsIntegerType();
		}

		public static bool IsFloatType(this PrimitiveType type)
		{
			return type switch {
				PrimitiveType.R4 or PrimitiveType.R8 or PrimitiveType.R => true,
				_ => false,
			};
		}

		/// <summary>
		/// Infers the C# type for an IL instruction.
		/// 
		/// Returns SpecialType.UnknownType for unsupported instructions.
		/// </summary>
		public static IType InferType(this ILInstruction inst, ICompilation? compilation)
		{
			switch (inst)
			{
				case NewObj newObj:
					return newObj.Method.DeclaringType ?? SpecialType.UnknownType;
				case NewArr newArr:
					if (compilation != null)
						return new ArrayType(compilation, newArr.Type, newArr.Indices.Count);
					else
						return SpecialType.UnknownType;
				case Call call:
					return call.Method.ReturnType;
				case CallVirt callVirt:
					return callVirt.Method.ReturnType;
				case CallIndirect calli:
					return calli.FunctionPointerType.ReturnType;
				case UserDefinedLogicOperator logicOp:
					return logicOp.Method.ReturnType;
				case LdObj ldobj:
					return ldobj.Type;
				case StObj stobj:
					return stobj.Type;
				case LdLoc ldloc:
					return ldloc.Variable.Type;
				case StLoc stloc:
					return stloc.Variable.Type;
				case LdLoca ldloca:
					return new ByReferenceType(ldloca.Variable.Type);
				case LdFlda ldflda:
					return new ByReferenceType(ldflda.Field.Type);
				case LdsFlda ldsflda:
					return new ByReferenceType(ldsflda.Field.Type);
				case LdElema ldelema:
					if (ldelema.Array.InferType(compilation) is ArrayType arrayType)
					{
						if (TypeUtils.IsCompatibleTypeForMemoryAccess(arrayType.ElementType, ldelema.Type))
						{
							return new ByReferenceType(arrayType.ElementType);
						}
					}
					return new ByReferenceType(ldelema.Type);
				case Comp comp:
					if (compilation == null)
						return SpecialType.UnknownType;
					return comp.LiftingKind switch {
						ComparisonLiftingKind.None or ComparisonLiftingKind.CSharp => compilation.FindType(KnownTypeCode.Boolean),
						ComparisonLiftingKind.ThreeValuedLogic => NullableType.Create(compilation, compilation.FindType(KnownTypeCode.Boolean)),
						_ => SpecialType.UnknownType,
					};
				case BinaryNumericInstruction bni:
					if (bni.IsLifted)
						return SpecialType.UnknownType;
					switch (bni.Operator)
					{
						case BinaryNumericOperator.BitAnd:
						case BinaryNumericOperator.BitOr:
						case BinaryNumericOperator.BitXor:
							var left = bni.Left.InferType(compilation);
							var right = bni.Right.InferType(compilation);
							if (left.Equals(right) && (left.IsCSharpPrimitiveIntegerType() || left.IsCSharpNativeIntegerType() || left.IsKnownType(KnownTypeCode.Boolean)))
								return left;
							else
								return SpecialType.UnknownType;
						default:
							return SpecialType.UnknownType;
					}
				default:
					return SpecialType.UnknownType;
			}
		}
	}
}
