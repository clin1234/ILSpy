// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.ControlFlow
{
	/// <summary>
	/// This exception is thrown when we find something else than we expect from the C# compiler.
	/// This aborts the analysis and makes the whole transform fail.
	/// </summary>
	class SymbolicAnalysisFailedException : Exception
	{
		public SymbolicAnalysisFailedException() { }
		public SymbolicAnalysisFailedException(string message) : base(message) { }
	}

	enum SymbolicValueType
	{
		/// <summary>
		/// Unknown value
		/// </summary>
		Unknown,

		/// <summary>
		/// int: Constant (result of ldc.i4)
		/// </summary>
		IntegerConstant,

		/// <summary>
		/// int: State + Constant
		/// </summary>
		State,

		/// <summary>
		/// This pointer (result of ldarg.0)
		/// </summary>
		This,

		/// <summary>
		/// bool: ValueSet.Contains(State)
		/// </summary>
		StateInSet,
	}

	internal readonly struct SymbolicValue
	{
		public readonly int Constant;
		public readonly SymbolicValueType Type;
		public readonly LongSet ValueSet;

		public SymbolicValue(SymbolicValueType type, int constant = 0) : this()
		{
			this.Type = type;
			this.Constant = constant;
		}

		public SymbolicValue(SymbolicValueType type, LongSet valueSet)
		{
			this.Type = type;
			this.Constant = 0;
			this.ValueSet = valueSet;
		}

		public SymbolicValue AsBool()
		{
			return Type == SymbolicValueType.State
				? new SymbolicValue(SymbolicValueType.StateInSet,
					new LongSet(unchecked(-Constant)).Invert())
				: this;
		}

		public override string ToString()
		{
			return $"[SymbolicValue {this.Type}: {this.Constant}]";
		}
	}

	class SymbolicEvaluationContext
	{
		static readonly SymbolicValue Failed = new SymbolicValue(SymbolicValueType.Unknown);
		readonly IField stateField;
		readonly List<ILVariable> stateVariables = new List<ILVariable>();

		public SymbolicEvaluationContext(IField stateField)
		{
			this.stateField = stateField;
		}

		public IEnumerable<ILVariable> StateVariables { get => stateVariables; }

		public void AddStateVariable(ILVariable v)
		{
			if (!stateVariables.Contains(v))
				stateVariables.Add(v);
		}

		public SymbolicValue Eval(ILInstruction inst)
		{
			if (inst is BinaryNumericInstruction { Operator: BinaryNumericOperator.Sub, CheckForOverflow: false } bni)
			{
				var left = Eval(bni.Left);
				var right = Eval(bni.Right);
				if (left.Type != SymbolicValueType.State && left.Type != SymbolicValueType.IntegerConstant)
					return Failed;
				return right.Type != SymbolicValueType.IntegerConstant
					? Failed
					: new SymbolicValue(left.Type,
						unchecked(left.Constant - right.Constant));
			}

			if (inst.MatchLdFld(out var target, out var field))
			{
				if (Eval(target).Type != SymbolicValueType.This)
					return Failed;
				return field.MemberDefinition != stateField ? Failed : new SymbolicValue(SymbolicValueType.State);
			}

			if (inst.MatchLdLoc(out var loadedVariable))
			{
				if (stateVariables.Contains(loadedVariable))
					return new SymbolicValue(SymbolicValueType.State);
				if (loadedVariable.Kind == VariableKind.Parameter && loadedVariable.Index < 0)
					return new SymbolicValue(SymbolicValueType.This);
				return Failed;
			}

			if (inst.MatchLdcI4(out var value))
			{
				return new SymbolicValue(SymbolicValueType.IntegerConstant, value);
			}

			if (inst is Comp comp)
			{
				var left = Eval(comp.Left);
				var right = Eval(comp.Right);
				if (left.Type == SymbolicValueType.State && right.Type == SymbolicValueType.IntegerConstant)
				{
					// bool: (state + left.Constant == right.Constant)
					LongSet trueSums =
						SwitchAnalysis.MakeSetWhereComparisonIsTrue(comp.Kind, right.Constant, comp.Sign);
					// symbolic value is true iff trueSums.Contains(state + left.Constant)
					LongSet trueStates = trueSums.AddOffset(unchecked(-left.Constant));
					// symbolic value is true iff trueStates.Contains(state)
					return new SymbolicValue(SymbolicValueType.StateInSet, trueStates);
				}

				if (left.Type == SymbolicValueType.StateInSet && right.Type == SymbolicValueType.IntegerConstant)
				{
					if (comp.Kind == ComparisonKind.Equality && right.Constant == 0)
					{
						// comp((x in set) == 0) ==> x not in set
						return new SymbolicValue(SymbolicValueType.StateInSet, left.ValueSet.Invert());
					}

					if (comp.Kind == ComparisonKind.Inequality && right.Constant != 0)
					{
						// comp((x in set) != 0) => x in set
						return new SymbolicValue(SymbolicValueType.StateInSet, left.ValueSet);
					}

					return Failed;
				}

				return Failed;
			}

			return Failed;
		}
	}
}