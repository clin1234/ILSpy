﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.IL.ControlFlow
{
	/// <summary>
	/// C# switch statements are not necessarily compiled into IL switch instructions.
	/// For example, when the label values are not contiguous, the C# compiler
	/// will generate if statements similar to a binary search.
	/// 
	/// This class analyses such sequences of if statements to reconstruct the original switch.
	/// </summary>
	/// <remarks>
	/// This analysis expects to be run on basic blocks (not extended basic blocks).
	/// </remarks>
	class SwitchAnalysis
	{
		/// <summary>
		/// Blocks that can be deleted if the tail of the initial block is replaced with a switch instruction.
		/// </summary>
		public readonly List<Block> InnerBlocks = new();

		/// <summary>
		/// Gets the sections that were detected by the previous AnalyzeBlock() call.
		/// </summary>
		public readonly List<KeyValuePair<LongSet, ILInstruction>> Sections = new();

		/// <summary>
		/// Used to de-duplicate sections with a branch instruction.
		/// Invariant: (Sections[targetBlockToSectionIndex[branch.TargetBlock]].Instruction as Branch).TargetBlock == branch.TargetBlock
		/// </summary>
		readonly Dictionary<Block, int> targetBlockToSectionIndex = new();

		/// <summary>
		/// Used to de-duplicate sections with a value-less leave instruction.
		/// Invariant: (Sections[targetBlockToSectionIndex[leave.TargetContainer]].Instruction as Leave).TargetContainer == leave.TargetContainer
		/// </summary>
		readonly Dictionary<BlockContainer, int> targetContainerToSectionIndex = new();

		/// <summary>
		/// The variable that is used to represent the switch expression.
		/// <c>null</c> while analyzing the first block.
		/// </summary>
		ILVariable switchVar;

		/// <summary>
		/// The variable to be used as the argument of the switch instruction.
		/// </summary>
		public ILVariable SwitchVariable => switchVar;

		/// <summary>
		/// Whether at least one the analyzed blocks contained an IL switch constructors.
		/// </summary>
		public bool ContainsILSwitch { get; private set; }

		public Block RootBlock { get; private set; }

		/// <summary>
		/// Gets/sets whether to allow unreachable cases in switch instructions.
		/// </summary>
		public bool AllowUnreachableCases { get; set; }

		/// <summary>
		/// Analyze the last two statements in the block and see if they can be turned into a
		/// switch instruction.
		/// </summary>
		/// <returns>true if the block could be analyzed successfully; false otherwise</returns>
		public bool AnalyzeBlock(Block block)
		{
			switchVar = null;
			RootBlock = block;
			targetBlockToSectionIndex.Clear();
			targetContainerToSectionIndex.Clear();
			Sections.Clear();
			InnerBlocks.Clear();
			ContainsILSwitch = false;
			return AnalyzeBlock(block, LongSet.Universe, tailOnly: true);
		}

		/// <summary>
		/// Analyzes the tail end (last two instructions) of a block.
		/// </summary>
		/// <remarks>
		/// Sets <c>switchVar</c> and <c>defaultInstruction</c> if they are null,
		/// and adds found sections to <c>sectionLabels</c> and <c>sectionInstructions</c>.
		/// 
		/// If the function returns false, <c>sectionLabels</c> and <c>sectionInstructions</c> are unmodified.
		/// </remarks>
		/// <param name="block">The block to analyze.</param>
		/// <param name="inputValues">The possible values of the "interesting" variable
		/// when control flow reaches this block.</param>
		/// <param name="tailOnly">If true, analyze only the tail (last two instructions).
		/// If false, analyze the whole block.</param>
		bool AnalyzeBlock(Block block, LongSet inputValues, bool tailOnly = false)
		{
			if (block.Instructions.Count == 0)
			{
				// might happen if the block was already marked for deletion in SwitchDetection
				return false;
			}

			if (tailOnly)
			{
				Debug.Assert(block == RootBlock);
			}
			else
			{
				Debug.Assert(switchVar != null); // switchVar should always be determined by the top-level call
				if (block.IncomingEdgeCount != 1 || block == RootBlock)
					return false; // for now, let's only consider if-structures that form a tree
				if (block.Parent != RootBlock.Parent)
					return false; // all blocks should belong to the same container
			}

			if (block.Instructions.Count >= 2
			    && block.Instructions[^2].MatchIfInstruction(out var condition, out var trueInst)
			    && AnalyzeCondition(condition, out LongSet trueValues)
			   )
			{
				if (!(tailOnly || block.Instructions.Count == 2))
					return false;
				trueValues = trueValues.IntersectWith(inputValues);
				if (trueValues.SetEquals(inputValues) || trueValues.IsEmpty)
					return false;
				if (trueInst.MatchBranch(out Block trueBlock) && AnalyzeBlock(trueBlock, trueValues))
				{
					// OK, true block was further analyzed.
					InnerBlocks.Add(trueBlock);
				}
				else
				{
					// Create switch section for trueInst.
					AddSection(trueValues, trueInst);
				}
			}
			else if (block.Instructions.Last() is SwitchInstruction switchInst)
			{
				if (!(tailOnly || block.Instructions.Count == 1))
					return false;
				if (AnalyzeSwitch(switchInst, inputValues))
				{
					ContainsILSwitch = true; // OK
					return true;
				}

				// switch analysis failed (e.g. switchVar mismatch)
				return false;
			}
			else
			{
				// unknown inst
				return false;
			}

			var remainingValues = inputValues.ExceptWith(trueValues);
			ILInstruction falseInst = block.Instructions.Last();
			if (falseInst.MatchBranch(out Block falseBlock) && AnalyzeBlock(falseBlock, remainingValues))
			{
				// OK, false block was further analyzed.
				InnerBlocks.Add(falseBlock);
			}
			else
			{
				// Create switch section for falseInst.
				AddSection(remainingValues, falseInst);
			}

			return true;
		}

		private bool AnalyzeSwitch(SwitchInstruction inst, LongSet inputValues)
		{
			Debug.Assert(!inst.IsLifted);
			long offset;
			if (MatchSwitchVar(inst.Value))
			{
				offset = 0;
			}
			else if (inst.Value is BinaryNumericInstruction bop)
			{
				if (bop.CheckForOverflow)
					return false;
				if (MatchSwitchVar(bop.Left) && bop.Right.MatchLdcI(out long val))
				{
					switch (bop.Operator)
					{
						case BinaryNumericOperator.Add:
							offset = unchecked(-val);
							break;
						case BinaryNumericOperator.Sub:
							offset = val;
							break;
						default: // unknown bop.Operator
							return false;
					}
				}
				else
				{
					// unknown bop.Left
					return false;
				}
			}
			else
			{
				// unknown inst.Value
				return false;
			}

			foreach (var section in inst.Sections)
			{
				var matchValues = section.Labels.AddOffset(offset).IntersectWith(inputValues);
				if (!AllowUnreachableCases && matchValues.IsEmpty)
					return false;
				if (matchValues.Count() > 1 && section.Body.MatchBranch(out var targetBlock) &&
				    AnalyzeBlock(targetBlock, matchValues))
				{
					InnerBlocks.Add(targetBlock);
				}
				else
				{
					AddSection(matchValues, section.Body);
				}
			}

			return true;
		}

		/// <summary>
		/// Adds a new section to the Sections list.
		/// 
		/// If the instruction is a branch instruction, unify the new section with an existing section
		/// that also branches to the same target.
		/// </summary>
		void AddSection(LongSet values, ILInstruction inst)
		{
			if (values.IsEmpty)
			{
				return;
			}

			if (inst.MatchBranch(out Block targetBlock))
			{
				if (targetBlockToSectionIndex.TryGetValue(targetBlock, out int index))
				{
					Sections[index] = new KeyValuePair<LongSet, ILInstruction>(
						Sections[index].Key.UnionWith(values),
						inst
					);
				}
				else
				{
					targetBlockToSectionIndex.Add(targetBlock, Sections.Count);
					Sections.Add(new KeyValuePair<LongSet, ILInstruction>(values, inst));
				}
			}
			else if (inst.MatchLeave(out BlockContainer targetContainer))
			{
				if (targetContainerToSectionIndex.TryGetValue(targetContainer, out int index))
				{
					Sections[index] = new KeyValuePair<LongSet, ILInstruction>(
						Sections[index].Key.UnionWith(values),
						inst
					);
				}
				else
				{
					targetContainerToSectionIndex.Add(targetContainer, Sections.Count);
					Sections.Add(new KeyValuePair<LongSet, ILInstruction>(values, inst));
				}
			}
			else
			{
				Sections.Add(new KeyValuePair<LongSet, ILInstruction>(values, inst));
			}
		}

		bool MatchSwitchVar(ILInstruction inst)
		{
			if (switchVar != null)
				return inst.MatchLdLoc(switchVar);
			return inst.MatchLdLoc(out switchVar);
		}

		bool MatchSwitchVar(ILInstruction inst, out long sub)
		{
			if (inst is BinaryNumericInstruction {
				    Operator: BinaryNumericOperator.Sub, CheckForOverflow: false, IsLifted: false
			    } bn && bn.Right.MatchLdcI(out sub))
			{
				return MatchSwitchVar(bn.Left);
			}

			sub = 0;
			return MatchSwitchVar(inst);
		}

		/// <summary>
		/// Analyzes the boolean condition, returning the set of values of the interesting
		/// variable for which the condition evaluates to true.
		/// </summary>
		private bool AnalyzeCondition(ILInstruction condition, out LongSet trueValues)
		{
			if (condition is Comp comp && MatchSwitchVar(comp.Left, out var sub) && comp.Right.MatchLdcI(out long val))
			{
				// if (comp((V - sub) OP val))
				trueValues = MakeSetWhereComparisonIsTrue(comp.Kind, val, comp.Sign);
				trueValues = trueValues.AddOffset(sub);
				return true;
			}

			if (MatchSwitchVar(condition))
			{
				// if (ldloc V) --> branch for all values except 0
				trueValues = new LongSet(0).Invert();
				return true;
			}

			if (condition.MatchLogicNot(out ILInstruction arg))
			{
				// if (logic.not(X)) --> branch for all values where if (X) does not branch
				bool res = AnalyzeCondition(arg, out LongSet falseValues);
				trueValues = falseValues.Invert();
				return res;
			}

			trueValues = LongSet.Empty;
			return false;
		}

		/// <summary>
		/// Create the LongSet that contains a value x iff x compared with value is true.
		/// </summary>
		internal static LongSet MakeSetWhereComparisonIsTrue(ComparisonKind kind, long val, Sign sign)
		{
			return kind switch {
				ComparisonKind.Equality => new LongSet(val),
				ComparisonKind.Inequality => new LongSet(val).Invert(),
				ComparisonKind.LessThan => MakeGreaterThanOrEqualSet(val, sign).Invert(),
				ComparisonKind.LessThanOrEqual => MakeLessThanOrEqualSet(val, sign),
				ComparisonKind.GreaterThan => MakeLessThanOrEqualSet(val, sign).Invert(),
				ComparisonKind.GreaterThanOrEqual => MakeGreaterThanOrEqualSet(val, sign),
				_ => throw new ArgumentException("Invalid ComparisonKind")
			};
		}

		private static LongSet MakeGreaterThanOrEqualSet(long val, Sign sign)
		{
			if (sign == Sign.Signed)
			{
				return new LongSet(LongInterval.Inclusive(val, long.MaxValue));
			}

			Debug.Assert(sign == Sign.Unsigned);
			if (val >= 0)
			{
				// The range val to ulong.MaxValue expressed with signed longs
				// is not a single contiguous range, but two ranges:
				return new LongSet(LongInterval.Inclusive(val, long.MaxValue))
					.UnionWith(new LongSet(new LongInterval(long.MinValue, 0)));
			}

			return new LongSet(new LongInterval(val, 0));
		}

		private static LongSet MakeLessThanOrEqualSet(long val, Sign sign)
		{
			if (sign == Sign.Signed)
			{
				return new LongSet(LongInterval.Inclusive(long.MinValue, val));
			}

			Debug.Assert(sign == Sign.Unsigned);
			if (val >= 0)
			{
				return new LongSet(LongInterval.Inclusive(0, val));
			}

			// The range 0 to (ulong)val expressed with signed longs
			// is not a single contiguous range, but two ranges:
			return new LongSet(LongInterval.Inclusive(0, long.MaxValue))
				.UnionWith(new LongSet(LongInterval.Inclusive(long.MinValue, val)));
		}
	}
}