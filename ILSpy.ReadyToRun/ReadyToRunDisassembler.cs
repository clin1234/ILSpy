// Copyright (c) 2018 Siegfried Pammer
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
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;

using Iced.Intel;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.Metadata;

using ILCompiler.Reflection.ReadyToRun;
using ILCompiler.Reflection.ReadyToRun.Amd64;

namespace ICSharpCode.ILSpy.ReadyToRun
{
	internal sealed class ReadyToRunDisassembler
	{
		private readonly ITextOutput output;
		private readonly ReadyToRunReader? reader;
		private readonly RuntimeFunction runtimeFunction;

		public ReadyToRunDisassembler(ITextOutput output, ReadyToRunReader? reader, RuntimeFunction runtimeFunction)
		{
			this.output = output;
			this.reader = reader;
			this.runtimeFunction = runtimeFunction;
		}

		public void Disassemble(PEFile currentFile, int bitness, ulong address, bool showMetadataTokens, bool showMetadataTokensInBase10)
		{
			// TODO: Decorate the disassembly with GCInfo
			ReadyToRunMethod readyToRunMethod = runtimeFunction.Method;
			WriteCommentLine(readyToRunMethod.SignatureString);

			Dictionary<ulong, UnwindCode>? unwindInfo = null;
			if (ReadyToRunOptions.GetIsShowUnwindInfo(null) && bitness == 64)
			{
				unwindInfo = WriteUnwindInfo();
			}

			bool isShowDebugInfo = ReadyToRunOptions.GetIsShowDebugInfo(null);
			DebugInfoHelper? debugInfo = null;
			if (isShowDebugInfo)
			{
				debugInfo = WriteDebugInfo();
			}

			byte[] codeBytes = new byte[runtimeFunction.Size];
			for (int i = 0; i < runtimeFunction.Size; i++)
			{
				codeBytes[i] = reader.Image[reader.GetOffset(runtimeFunction.StartAddress) + i];
			}

			var codeReader = new ByteArrayCodeReader(codeBytes);
			var decoder = Decoder.Create(bitness, codeReader);
			decoder.IP = address;
			ulong endRip = decoder.IP + (uint)codeBytes.Length;

			var instructions = new InstructionList();
			while (decoder.IP < endRip)
			{
				decoder.Decode(out instructions.AllocUninitializedElement());
			}

			string disassemblyFormat = ReadyToRunOptions.GetDisassemblyFormat(null);
			Formatter formatter;
			if (disassemblyFormat.Equals(ReadyToRunOptions.intel))
			{
				formatter = new NasmFormatter();
			}
			else
			{
				Debug.Assert(disassemblyFormat.Equals(ReadyToRunOptions.gas));
				formatter = new GasFormatter();
			}
			formatter.Options.DigitSeparator = "`";
			formatter.Options.FirstOperandCharIndex = 10;
			var tempOutput = new StringOutput();
			ulong baseInstrIP = instructions[0].IP;
			foreach (var instr in instructions)
			{
				int byteBaseIndex = (int)(instr.IP - address);
				if (isShowDebugInfo && runtimeFunction.DebugInfo != null)
				{
					foreach (var bound in runtimeFunction.DebugInfo.BoundsList)
					{
						if (bound.NativeOffset == byteBaseIndex)
						{
							switch (bound.ILOffset)
							{
								case (uint)DebugInfoBoundsType.Prolog:
									WriteCommentLine("Prolog");
									break;
								case (uint)DebugInfoBoundsType.Epilog:
									WriteCommentLine("Epilog");
									break;
								default:
									WriteCommentLine($"IL_{bound.ILOffset:x4}");
									break;
							}
						}
					}
				}
				formatter.Format(instr, tempOutput);
				output.Write(instr.IP.ToString("X16"));
				output.Write(" ");
				int instrLen = instr.Length;
				for (int i = 0; i < instrLen; i++)
				{
					output.Write(codeBytes[byteBaseIndex + i].ToString("X2"));
				}
				int missingBytes = 10 - instrLen;
				for (int i = 0; i < missingBytes; i++)
				{
					output.Write("  ");
				}
				output.Write(" ");
				output.Write(tempOutput.ToStringAndReset());
				DecorateUnwindInfo(unwindInfo, baseInstrIP, instr);
				DecorateDebugInfo(instr, debugInfo, baseInstrIP);

				DecorateCallSite(currentFile, showMetadataTokens, showMetadataTokensInBase10, instr);
			}
			output.WriteLine();
		}

		private void WriteCommentLine(string comment)
		{
			output.WriteLine("; " + comment);
		}

		private sealed class NativeVarInfoRecord
		{
			public ulong codeOffset;
			public bool isStart;
			public bool isRegRelative;
			public string? register;
			public int registerOffset;
			public Variable? variable;
		}

		private sealed class DebugInfoHelper
		{
			public List<NativeVarInfoRecord>? records;
			private int i;
			public readonly Dictionary<string, Dictionary<int, HashSet<Variable>>> registerRelativeVariables;
			public readonly Dictionary<string, HashSet<Variable?>> registerVariables;

			public DebugInfoHelper()
			{
				this.registerRelativeVariables = new Dictionary<string, Dictionary<int, HashSet<Variable>>>();
				this.registerVariables = new Dictionary<string, HashSet<Variable?>>();
			}

			public void Update(ulong codeOffset)
			{
				while (i < records.Count && records[i].codeOffset == codeOffset)
				{
					HashSet<Variable?> variables;
					if (records[i].isRegRelative)
					{
						Dictionary<int, HashSet<Variable>> offsetToVariableMap;
						if (records[i].isStart)
						{
							if (!this.registerRelativeVariables.TryGetValue(records[i].register, out offsetToVariableMap))
							{
								offsetToVariableMap = new Dictionary<int, HashSet<Variable>>();
								this.registerRelativeVariables.Add(records[i].register, offsetToVariableMap);
							}
							if (!offsetToVariableMap.TryGetValue(records[i].registerOffset, out variables))
							{
								variables = new HashSet<Variable?>();
								offsetToVariableMap.Add(records[i].registerOffset, variables);
							}
							variables.Add(records[i].variable);
						}
						else
						{
							offsetToVariableMap = this.registerRelativeVariables[records[i].register];
							variables = offsetToVariableMap[records[i].registerOffset];
							variables.Remove(records[i].variable);
						}
					}
					else
					{
						if (records[i].isStart)
						{
							if (!this.registerVariables.TryGetValue(records[i].register, out variables))
							{
								variables = new HashSet<Variable?>();
								this.registerVariables.Add(records[i].register, variables);
							}
							variables.Add(records[i].variable);
						}
						else
						{
							// If the optimizing compiler decides that two variables will always have the same value within a basic block
							// It might assign the same location for two variables.

							// The compiler also generates potentially wrong 1 byte long debug info record for arguments in prolog.
							// These record might describe the same variable in overlapping ranges.
							// See https://cshung.github.io/posts/debug-info-debugging/ for the investigation.
							variables = this.registerVariables[records[i].register];
							variables.Remove(records[i].variable);
						}
					}
					i++;
				}
			}
		}

		private DebugInfoHelper WriteDebugInfo()
		{
			List<NativeVarInfoRecord> records = new List<NativeVarInfoRecord>();
			foreach (RuntimeFunction runtimeFunction in runtimeFunction.Method.RuntimeFunctions)
			{
				DebugInfo debugInfo = runtimeFunction.DebugInfo;
				if (debugInfo != null && debugInfo.BoundsList.Count > 0)
				{
					foreach (var varLoc in debugInfo.VariablesList)
					{
						if (varLoc.StartOffset == varLoc.EndOffset)
						{
							// This could happen if the compiler is generating bogus variable info mapping record that covers 0 instructions
							// See https://github.com/dotnet/runtime/issues/47202
							// Debug.Assert(false);
							continue;
						}
						switch (varLoc.VariableLocation.VarLocType)
						{
							case VarLocType.VLT_STK:
							case VarLocType.VLT_STK_BYREF:
								records.Add(new NativeVarInfoRecord {
									codeOffset = varLoc.StartOffset,
									isStart = true,
									isRegRelative = true,
									register = DebugInfo.GetPlatformSpecificRegister(debugInfo.Machine, varLoc.VariableLocation.Data1),
									registerOffset = varLoc.VariableLocation.Data2,
									variable = varLoc.Variable
								});
								records.Add(new NativeVarInfoRecord {
									codeOffset = varLoc.EndOffset,
									isStart = false,
									isRegRelative = true,
									register = DebugInfo.GetPlatformSpecificRegister(debugInfo.Machine, varLoc.VariableLocation.Data1),
									registerOffset = varLoc.VariableLocation.Data2,
									variable = varLoc.Variable
								});
								break;
							case VarLocType.VLT_REG:
								records.Add(new NativeVarInfoRecord {
									codeOffset = varLoc.StartOffset,
									isStart = true,
									isRegRelative = false,
									register = DebugInfo.GetPlatformSpecificRegister(debugInfo.Machine, varLoc.VariableLocation.Data1),
									variable = varLoc.Variable
								});
								records.Add(new NativeVarInfoRecord {
									codeOffset = varLoc.EndOffset,
									isStart = false,
									isRegRelative = false,
									register = DebugInfo.GetPlatformSpecificRegister(debugInfo.Machine, varLoc.VariableLocation.Data1),
									variable = varLoc.Variable
								});
								break;
							default:
								// TODO
								break;
						}
					}
				}
			}

			records.Sort((x, y) => {
				if (x.codeOffset < y.codeOffset)
				{
					return -1;
				}
				else if (x.codeOffset > y.codeOffset)
				{
					return 1;
				}
				else
				{
					return x.isStart switch {
						false when y.isStart => -1,
						true when !y.isStart => 1,
						_ => 0
					};
				}
			});
			return new DebugInfoHelper {
				records = records
			};
		}

		private Dictionary<ulong, UnwindCode> WriteUnwindInfo()
		{
			Dictionary<ulong, UnwindCode> unwindCodes = new Dictionary<ulong, UnwindCode>();
			if (runtimeFunction.UnwindInfo is UnwindInfo amd64UnwindInfo)
			{
				string parsedFlags = "";
				if ((amd64UnwindInfo.Flags & (int)UnwindFlags.UNW_FLAG_EHANDLER) != 0)
				{
					parsedFlags += " EHANDLER";
				}
				if ((amd64UnwindInfo.Flags & (int)UnwindFlags.UNW_FLAG_UHANDLER) != 0)
				{
					parsedFlags += " UHANDLER";
				}
				if ((amd64UnwindInfo.Flags & (int)UnwindFlags.UNW_FLAG_CHAININFO) != 0)
				{
					parsedFlags += " CHAININFO";
				}
				if (parsedFlags.Length == 0)
				{
					parsedFlags = " NHANDLER";
				}
				WriteCommentLine($"UnwindInfo:");
				WriteCommentLine($"Version:            {amd64UnwindInfo.Version}");
				WriteCommentLine($"Flags:              0x{amd64UnwindInfo.Flags:X2}{parsedFlags}");
				WriteCommentLine($"FrameRegister:      {((amd64UnwindInfo.FrameRegister == 0) ? "none" : amd64UnwindInfo.FrameRegister.ToString().ToLower())}");
				foreach (var t in amd64UnwindInfo.UnwindCodes)
				{
					unwindCodes.Add(t.CodeOffset, t);
				}
			}
			return unwindCodes;
		}

		private void DecorateUnwindInfo(Dictionary<ulong, UnwindCode>? unwindInfo, ulong baseInstrIP, Instruction instr)
		{
			ulong nextInstructionOffset = instr.NextIP - baseInstrIP;
			if (unwindInfo != null && unwindInfo.ContainsKey(nextInstructionOffset))
			{
				UnwindCode unwindCode = unwindInfo[nextInstructionOffset];
				output.Write($" ; {unwindCode.UnwindOp}({unwindCode.OpInfoStr})");
			}
		}

		private void DecorateDebugInfo(Instruction instr, DebugInfoHelper? debugRecords, ulong baseInstrIP)
		{
			if (debugRecords != null)
			{
				HashSet<Variable> variables;
				InstructionInfoFactory factory = new InstructionInfoFactory();
				InstructionInfo info = factory.GetInfo(instr);
				ulong codeOffset = instr.IP - baseInstrIP;
				debugRecords.Update(codeOffset);
				foreach (UsedMemory usedMemInfo in info.GetUsedMemory())
				{
					int displacement;
					unchecked
					{ displacement = (int)usedMemInfo.Displacement; }

					if (debugRecords.registerRelativeVariables.TryGetValue(usedMemInfo.Base.ToString(), out Dictionary<int, HashSet<Variable>> offsetToVariableMap))
					{
						if (offsetToVariableMap.TryGetValue(displacement, out variables))
						{
							output.Write($";");
							foreach (Variable? variable in variables)
							{
								output.Write($" [{usedMemInfo.Base.ToString().ToLower()}{(displacement < 0 ? '-' : '+')}{Math.Abs(displacement):X}h] = {variable.Type} {variable.Index}");
							}
						}
					}
				}
				foreach (UsedRegister usedMemInfo in info.GetUsedRegisters())
				{
					// TODO, if the code is accessing EAX but the debug info maps to RAX, then this match is going to fail.
					if (debugRecords.registerVariables.TryGetValue(usedMemInfo.Register.ToString(), out variables))
					{
						output.Write($";");
						foreach (Variable? variable in variables)
						{
							output.Write($" {usedMemInfo.Register.ToString().ToLower()} = {variable.Type} {variable.Index}");
						}
					}
				}
			}
		}

		private void DecorateCallSite(PEFile currentFile, bool showMetadataTokens, bool showMetadataTokensInBase10, Instruction instr)
		{
			if (instr.IsCallNearIndirect)
			{
				int importCellAddress = (int)instr.IPRelativeMemoryAddress;
				if (reader.ImportSignatures.ContainsKey(importCellAddress))
				{
					output.Write(" ; ");
					ReadyToRunSignature signature = reader.ImportSignatures[importCellAddress];
					switch (signature)
					{
						case MethodDefEntrySignature methodDefSignature:
							var methodDefToken = MetadataTokens.EntityHandle(unchecked((int)methodDefSignature.MethodDefToken));
							if (showMetadataTokens)
							{
								output.WriteReference(currentFile, methodDefToken,
									showMetadataTokensInBase10
										? $"({MetadataTokens.GetToken(methodDefToken)}) "
										: $"({MetadataTokens.GetToken(methodDefToken):X8}) ", "metadata");
							}
							methodDefToken.WriteTo(currentFile, output, default);
							break;
						case MethodRefEntrySignature methodRefSignature:
							var methodRefToken = MetadataTokens.EntityHandle(unchecked((int)methodRefSignature.MethodRefToken));
							if (showMetadataTokens)
							{
								output.WriteReference(currentFile, methodRefToken,
									showMetadataTokensInBase10
										? $"({MetadataTokens.GetToken(methodRefToken)}) "
										: $"({MetadataTokens.GetToken(methodRefToken):X8}) ", "metadata");
							}
							methodRefToken.WriteTo(currentFile, output, default);
							break;
						default:
							output.WriteLine(reader.ImportSignatures[importCellAddress].ToString(new SignatureFormattingOptions()));
							break;
					}
					output.WriteLine();
				}
			}
			else
			{
				output.WriteLine();
			}
		}
	}
}