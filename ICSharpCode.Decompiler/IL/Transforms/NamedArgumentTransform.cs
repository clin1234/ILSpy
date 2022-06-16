﻿using System.Diagnostics;
using System.Linq;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.IL.Transforms
{
	public sealed class NamedArgumentTransform : IStatementTransform
	{
		public void Run(Block block, int pos, StatementTransformContext context)
		{
			if (!context.Settings.NamedArguments)
				return;
			var options = ILInlining.OptionsForBlock(block, pos, context);
			options |= InliningOptions.IntroduceNamedArguments;
			ILInlining.InlineOneIfPossible(block, pos, options, context: context);
		}

		internal static ILInlining.FindResult CanIntroduceNamedArgument(CallInstruction call, ILInstruction? child,
			ILVariable v, ILInstruction expressionBeingMoved)
		{
			Debug.Assert(child.Parent == call);
			if (call.IsInstanceCall && child.ChildIndex == 0)
				return
					ILInlining.FindResult.Stop; // cannot use named arg to move expressionBeingMoved before this pointer
			if (call.Method.IsOperator || call.Method.IsAccessor)
				return ILInlining.FindResult.Stop; // cannot use named arg for operators or accessors
			if (call.Method is VarArgInstanceMethod)
				return ILInlining.FindResult.Stop; // CallBuilder doesn't support named args when using varargs
			if (call.Method.IsConstructor)
			{
				IType type = call.Method.DeclaringType;
				if (type.Kind == TypeKind.Delegate || type.IsAnonymousType())
					return ILInlining.FindResult.Stop;
			}

			if (call.Method.Parameters.Any(p => string.IsNullOrEmpty(p.Name)))
				return ILInlining.FindResult.Stop; // cannot use named arguments
			for (int i = child.ChildIndex; i < call.Arguments.Count; i++)
			{
				var r = ILInlining.FindLoadInNext(call.Arguments[i], v, expressionBeingMoved, InliningOptions.None);
				if (r.Type == ILInlining.FindResultType.Found)
				{
					return ILInlining.FindResult.NamedArgument(r.LoadInst, call.Arguments[i]);
				}
			}

			return ILInlining.FindResult.Stop;
		}

		internal static ILInlining.FindResult CanExtendNamedArgument(Block block, ILVariable v,
			ILInstruction expressionBeingMoved)
		{
			Debug.Assert(block.Kind == BlockKind.CallWithNamedArgs);
			var firstArg = ((StLoc)block.Instructions[0]).Value;
			var r = ILInlining.FindLoadInNext(firstArg, v, expressionBeingMoved,
				InliningOptions.IntroduceNamedArguments);
			if (r.Type is ILInlining.FindResultType.Found or ILInlining.FindResultType.NamedArgument)
			{
				return r; // OK, inline into first instruction of block
			}

			var call = (CallInstruction)block.FinalInstruction;
			if (call.IsInstanceCall)
			{
				// For instance calls, block.Instructions[0] is the argument
				// for the 'this' pointer. We can only insert at position 1.
				if (r.Type == ILInlining.FindResultType.Stop)
				{
					// error: can't move expressionBeingMoved after block.Instructions[0]
					return ILInlining.FindResult.Stop;
				}

				// Because we always ensure block.Instructions[0] is the 'this' argument,
				// it's possible that the place we actually need to inline into
				// is within block.Instructions[1]:
				if (block.Instructions.Count > 1)
				{
					r = ILInlining.FindLoadInNext(block.Instructions[1], v, expressionBeingMoved,
						InliningOptions.IntroduceNamedArguments);
					if (r.Type is ILInlining.FindResultType.Found or ILInlining.FindResultType.NamedArgument)
					{
						return r; // OK, inline into block.Instructions[1]
					}
				}
			}

			foreach (var arg in call.Arguments)
			{
				if (arg.MatchLdLoc(v))
				{
					return ILInlining.FindResult.NamedArgument(arg, arg);
				}
			}

			return ILInlining.FindResult.Stop;
		}

		/// <summary>
		/// Introduce a named argument for 'arg' and evaluate it before the other arguments
		/// (except for the "this" pointer)
		/// </summary>
		internal static void IntroduceNamedArgument(ILInstruction? arg, ILTransformContext context)
		{
			var call = (CallInstruction)arg.Parent;
			Debug.Assert(context.Function == call.Ancestors.OfType<ILFunction>().First());
			var type = context.TypeSystem.FindType(arg.ResultType);
			var v = context.Function.RegisterVariable(VariableKind.NamedArgument, type);
			context.Step($"Introduce named argument '{v.Name}'", arg);
			if (call.Parent is not Block { Kind: BlockKind.CallWithNamedArgs } namedArgBlock)
			{
				// create namedArgBlock:
				namedArgBlock = new Block(BlockKind.CallWithNamedArgs);
				call.ReplaceWith(namedArgBlock);
				namedArgBlock.FinalInstruction = call;
				if (call.IsInstanceCall)
				{
					IType thisVarType = call.ConstrainedTo ?? call.Method.DeclaringType;
					if (CallInstruction.ExpectedTypeForThisPointer(thisVarType) == StackType.Ref)
					{
						thisVarType = new ByReferenceType(thisVarType);
					}

					var thisArgVar =
						context.Function.RegisterVariable(VariableKind.NamedArgument, thisVarType, "this_arg");
					namedArgBlock.Instructions.Add(new StLoc(thisArgVar, call.Arguments[0]));
					call.Arguments[0] = new LdLoc(thisArgVar);
				}
			}

			int argIndex = arg.ChildIndex;
			Debug.Assert(call.Arguments[argIndex] == arg);
			namedArgBlock.Instructions.Insert(call.IsInstanceCall ? 1 : 0, new StLoc(v, arg));
			call.Arguments[argIndex] = new LdLoc(v);
		}
	}
}