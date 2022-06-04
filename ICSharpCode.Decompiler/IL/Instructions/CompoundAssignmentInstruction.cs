﻿#nullable enable
// Copyright (c) 2016 Siegfried Pammer
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
using System.Diagnostics;
using System.Linq.Expressions;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.IL
{
	public enum CompoundEvalMode : byte
	{
		/// <summary>
		/// The compound.assign instruction will evaluate to the old value.
		/// This mode is used only for post-increment/decrement.
		/// </summary>
		EvaluatesToOldValue,

		/// <summary>
		/// The compound.assign instruction will evaluate to the new value.
		/// This mode is used for compound assignments and pre-increment/decrement.
		/// </summary>
		EvaluatesToNewValue
	}

	public enum CompoundTargetKind : byte
	{
		/// <summary>
		/// The target is an instruction computing an address,
		/// and the compound.assign will implicitly load/store from/to that address.
		/// </summary>
		Address,

		/// <summary>
		/// The Target must be a call to a property getter,
		/// and the compound.assign will implicitly call the corresponding property setter.
		/// </summary>
		Property,

		/// <summary>
		/// The target is a dynamic call.
		/// </summary>
		Dynamic
	}

	public abstract partial class CompoundAssignmentInstruction : ILInstruction
	{
		public readonly CompoundEvalMode EvalMode;

		/// <summary>
		/// If TargetIsProperty is true, the Target must be a call to a property getter,
		/// and the compound.assign will implicitly call the corresponding property setter.
		/// Otherwise, the Target can be any instruction that evaluates to an address,
		/// and the compound.assign will implicit load and store from/to that address.
		/// </summary>
		public readonly CompoundTargetKind TargetKind;

		public CompoundAssignmentInstruction(OpCode opCode, CompoundEvalMode evalMode, ILInstruction target,
			CompoundTargetKind targetKind, ILInstruction value)
			: base(opCode)
		{
			this.EvalMode = evalMode;
			this.Target = target;
			this.TargetKind = targetKind;
			this.Value = value;
			CheckValidTarget();
		}

		internal override void CheckInvariant(ILPhase phase)
		{
			base.CheckInvariant(phase);
			CheckValidTarget();
		}

		[Conditional("DEBUG")]
		void CheckValidTarget()
		{
			switch (TargetKind)
			{
				case CompoundTargetKind.Address:
					Debug.Assert(target.ResultType is StackType.Ref or StackType.I);
					break;
				case CompoundTargetKind.Property:
					Debug.Assert(target.OpCode is OpCode.Call or OpCode.CallVirt);
					Debug.Assert(((CallInstruction)target).Method.AccessorOwner is IProperty { CanSet: true });
					break;
				case CompoundTargetKind.Dynamic:
					Debug.Assert(target.OpCode is OpCode.DynamicGetMemberInstruction
						or OpCode.DynamicGetIndexInstruction);
					break;
			}
		}

		protected void WriteSuffix(ITextOutput output)
		{
			switch (TargetKind)
			{
				case CompoundTargetKind.Address:
					output.Write(".address");
					break;
				case CompoundTargetKind.Property:
					output.Write(".property");
					break;
			}

			switch (EvalMode)
			{
				case CompoundEvalMode.EvaluatesToNewValue:
					output.Write(".new");
					break;
				case CompoundEvalMode.EvaluatesToOldValue:
					output.Write(".old");
					break;
			}
		}
	}

	public partial class NumericCompoundAssign : CompoundAssignmentInstruction, ILiftableInstruction
	{
		/// <summary>
		/// Gets whether the instruction checks for overflow.
		/// </summary>
		public readonly bool CheckForOverflow;

		public readonly StackType LeftInputType;

		/// <summary>
		/// The operator used by this assignment operator instruction.
		/// </summary>
		public readonly BinaryNumericOperator Operator;

		public readonly StackType RightInputType;

		/// <summary>
		/// For integer operations that depend on the sign, specifies whether the operation
		/// is signed or unsigned.
		/// For instructions that produce the same result for either sign, returns Sign.None.
		/// </summary>
		public readonly Sign Sign;

		public NumericCompoundAssign(BinaryNumericInstruction binary, ILInstruction target,
			CompoundTargetKind targetKind, ILInstruction value, IType type, CompoundEvalMode evalMode)
			: base(OpCode.NumericCompoundAssign, evalMode, target, targetKind, value)
		{
			Debug.Assert(IsBinaryCompatibleWithType(binary, type, null));
			this.CheckForOverflow = binary.CheckForOverflow;
			this.Sign = binary.Sign;
			this.LeftInputType = binary.LeftInputType;
			this.RightInputType = binary.RightInputType;
			this.UnderlyingResultType = binary.UnderlyingResultType;
			this.Operator = binary.Operator;
			this.IsLifted = binary.IsLifted;
			this.type = type;
			this.AddILRange(binary);
			Debug.Assert(evalMode == CompoundEvalMode.EvaluatesToNewValue ||
			             Operator is BinaryNumericOperator.Add or BinaryNumericOperator.Sub);
			Debug.Assert(this.ResultType == (IsLifted ? StackType.O : UnderlyingResultType));
		}

		public override InstructionFlags DirectFlags {
			get {
				var flags = InstructionFlags.SideEffect;
				if (CheckForOverflow || Operator is BinaryNumericOperator.Div or BinaryNumericOperator.Rem)
					flags |= InstructionFlags.MayThrow;
				return flags;
			}
		}

		public StackType UnderlyingResultType { get; }

		public bool IsLifted { get; }

		/// <summary>
		/// Gets whether the specific binary instruction is compatible with a compound operation on the specified type.
		/// </summary>
		internal static bool IsBinaryCompatibleWithType(BinaryNumericInstruction binary, IType type,
			DecompilerSettings? settings)
		{
			if (binary.IsLifted)
			{
				if (!NullableType.IsNullable(type))
					return false;
				type = NullableType.GetUnderlyingType(type);
			}

			if (type.Kind == TypeKind.Unknown)
			{
				return false; // avoid introducing a potentially-incorrect compound assignment
			}
			else if (type.Kind == TypeKind.Enum)
			{
				switch (binary.Operator)
				{
					case BinaryNumericOperator.Add:
					case BinaryNumericOperator.Sub:
					case BinaryNumericOperator.BitAnd:
					case BinaryNumericOperator.BitOr:
					case BinaryNumericOperator.BitXor:
						break; // OK
					default:
						return false; // operator not supported on enum types
				}
			}
			else if (type.Kind == TypeKind.Pointer)
			{
				switch (binary.Operator)
				{
					case BinaryNumericOperator.Add:
					case BinaryNumericOperator.Sub:
						// ensure that the byte offset is a multiple of the pointer size
						return PointerArithmeticOffset.Detect(
							binary.Right,
							((PointerType)type).ElementType,
							checkForOverflow: binary.CheckForOverflow
						) != null;
					default:
						return false; // operator not supported on pointer types
				}
			}
			else if (type.IsKnownType(KnownTypeCode.IntPtr) || type.IsKnownType(KnownTypeCode.UIntPtr))
			{
				// "target.intptr *= 2;" is compiler error, but
				// "target.intptr *= (nint)2;" works
				if (settings is { NativeIntegers: false })
				{
					// But if native integers are not available, we cannot use compound assignment.
					return false;
				}

				// The trick with casting the RHS to n(u)int doesn't work for shifts:
				switch (binary.Operator)
				{
					case BinaryNumericOperator.ShiftLeft:
					case BinaryNumericOperator.ShiftRight:
						return false;
				}
			}

			if (binary.Sign != Sign.None)
			{
				if (type.IsCSharpSmallIntegerType())
				{
					// C# will use numeric promotion to int, binary op must be signed
					if (binary.Sign != Sign.Signed)
						return false;
				}
				else
				{
					// C# will use sign from type
					if (type.GetSign() != binary.Sign)
						return false;
				}
			}

			// Can't transform if the RHS value would be need to be truncated for the LHS type.
			if (Transforms.TransformAssignment.IsImplicitTruncation(binary.Right, type, null, binary.IsLifted))
				return false;
			return true;
		}

		protected override InstructionFlags ComputeFlags()
		{
			var flags = Target.Flags | Value.Flags | InstructionFlags.SideEffect;
			if (CheckForOverflow || Operator is BinaryNumericOperator.Div or BinaryNumericOperator.Rem)
				flags |= InstructionFlags.MayThrow;
			return flags;
		}

		public override void WriteTo(ITextOutput output, ILAstWritingOptions options)
		{
			WriteILRange(output, options);
			output.Write(OpCode);
			output.Write("." + BinaryNumericInstruction.GetOperatorName(Operator));
			if (CheckForOverflow)
			{
				output.Write(".ovf");
			}

			if (Sign == Sign.Unsigned)
			{
				output.Write(".unsigned");
			}
			else if (Sign == Sign.Signed)
			{
				output.Write(".signed");
			}

			output.Write('.');
			output.Write(UnderlyingResultType.ToString().ToLowerInvariant());
			if (IsLifted)
			{
				output.Write(".lifted");
			}

			base.WriteSuffix(output);
			output.Write('(');
			Target.WriteTo(output, options);
			output.Write(", ");
			Value.WriteTo(output, options);
			output.Write(')');
		}
	}

	public partial class UserDefinedCompoundAssign : CompoundAssignmentInstruction
	{
		public readonly IMethod Method;

		public UserDefinedCompoundAssign(IMethod method, CompoundEvalMode evalMode,
			ILInstruction target, CompoundTargetKind targetKind, ILInstruction value)
			: base(OpCode.UserDefinedCompoundAssign, evalMode, target, targetKind, value)
		{
			this.Method = method;
			Debug.Assert(Method.IsOperator || IsStringConcat(method));
			Debug.Assert(evalMode == CompoundEvalMode.EvaluatesToNewValue ||
			             Method.Name is "op_Increment" or "op_Decrement");
		}

		public bool IsLifted => false; // TODO: implement lifted user-defined compound assignments

		public override StackType ResultType => Method.ReturnType.GetStackType();

		public static bool IsStringConcat(IMethod method)
		{
			return method.Name == "Concat" && method.IsStatic && method.DeclaringType.IsKnownType(KnownTypeCode.String);
		}

		public override void WriteTo(ITextOutput output, ILAstWritingOptions options)
		{
			WriteILRange(output, options);
			output.Write(OpCode);
			base.WriteSuffix(output);
			output.Write(' ');
			Method.WriteTo(output);
			output.Write('(');
			this.Target.WriteTo(output, options);
			output.Write(", ");
			this.Value.WriteTo(output, options);
			output.Write(')');
		}
	}

	public partial class DynamicCompoundAssign : CompoundAssignmentInstruction
	{
		public DynamicCompoundAssign(ExpressionType op, CSharpBinderFlags binderFlags,
			ILInstruction target, CSharpArgumentInfo targetArgumentInfo,
			ILInstruction value, CSharpArgumentInfo valueArgumentInfo,
			CompoundTargetKind targetKind = CompoundTargetKind.Dynamic)
			: base(OpCode.DynamicCompoundAssign, CompoundEvalModeFromOperation(op), target, targetKind, value)
		{
			if (!IsExpressionTypeSupported(op))
				throw new ArgumentOutOfRangeException(nameof(op));
			this.BinderFlags = binderFlags;
			this.Operation = op;
			this.TargetArgumentInfo = targetArgumentInfo;
			this.ValueArgumentInfo = valueArgumentInfo;
		}

		public ExpressionType Operation { get; }
		public CSharpArgumentInfo TargetArgumentInfo { get; }
		public CSharpArgumentInfo ValueArgumentInfo { get; }
		public CSharpBinderFlags BinderFlags { get; }

		public override void WriteTo(ITextOutput output, ILAstWritingOptions options)
		{
			WriteILRange(output, options);
			output.Write(OpCode);
			output.Write("." + Operation.ToString().ToLower());
			DynamicInstruction.WriteBinderFlags(BinderFlags, output, options);
			base.WriteSuffix(output);
			output.Write(' ');
			DynamicInstruction.WriteArgumentList(output, options, (Target, TargetArgumentInfo),
				(Value, ValueArgumentInfo));
		}

		internal static bool IsExpressionTypeSupported(ExpressionType type)
		{
			return type is ExpressionType.AddAssign or ExpressionType.AddAssignChecked or ExpressionType.AndAssign
				or ExpressionType.DivideAssign or ExpressionType.ExclusiveOrAssign or ExpressionType.LeftShiftAssign
				or ExpressionType.ModuloAssign or ExpressionType.MultiplyAssign or ExpressionType.MultiplyAssignChecked
				or ExpressionType.OrAssign or ExpressionType.PostDecrementAssign or ExpressionType.PostIncrementAssign
				or ExpressionType.PreDecrementAssign or ExpressionType.PreIncrementAssign
				or ExpressionType.RightShiftAssign or ExpressionType.SubtractAssign
				or ExpressionType.SubtractAssignChecked;
		}

		static CompoundEvalMode CompoundEvalModeFromOperation(ExpressionType op)
		{
			switch (op)
			{
				case ExpressionType.PostIncrementAssign:
				case ExpressionType.PostDecrementAssign:
					return CompoundEvalMode.EvaluatesToOldValue;
				default:
					return CompoundEvalMode.EvaluatesToNewValue;
			}
		}
	}
}