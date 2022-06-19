﻿// Copyright (c) 2018 Siegfried Pammer
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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;

using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Util;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;
using SRM = System.Reflection.Metadata;

#nullable enable

namespace ICSharpCode.ILSpyX.PdbProvider
{
	internal sealed class MonoCecilDebugInfoProvider : IDebugInfoProvider
	{
		readonly Dictionary<SRM.MethodDefinitionHandle, (IList<SequencePoint> SequencePoints, IList<Variable> Variables
			)> debugInfo;

		public unsafe MonoCecilDebugInfoProvider(PEFile module, string? pdbFileName, string? description = null)
		{
			if (module == null) throw new ArgumentNullException(nameof(module));

			if (!module.Reader.IsEntireImageAvailable)
			{
				throw new ArgumentException("This provider needs access to the full image!");
			}

			this.SourceFileName = pdbFileName ?? throw new ArgumentNullException(nameof(pdbFileName));
			this.Description = description ?? $"Loaded from PDB file: {pdbFileName}";

			var image = module.Reader.GetEntireImage();
			this.debugInfo = new Dictionary<SRM.MethodDefinitionHandle, (IList<SequencePoint> SequencePoints,
				IList<Variable> Variables)>();
			using UnmanagedMemoryStream stream = new(image.Pointer, image.Length);
			using ModuleDefinition? moduleDef = ModuleDefinition.ReadModule(stream);
			moduleDef.ReadSymbols(new PdbReaderProvider().GetSymbolReader(moduleDef, pdbFileName));

			foreach (SRM.MethodDefinitionHandle method in module.Metadata.MethodDefinitions)
			{
				MethodDefinition? cecilMethod =
					moduleDef.LookupToken(MetadataTokens.GetToken(method)) as MethodDefinition;
				MethodDebugInformation? methodDebugInfo = cecilMethod?.DebugInformation;
				if (methodDebugInfo == null)
				{
					continue;
				}

				IList<SequencePoint> sequencePoints = EmptyList<SequencePoint>.Instance;
				if (methodDebugInfo.HasSequencePoints)
				{
					sequencePoints = new List<SequencePoint>(methodDebugInfo.SequencePoints.Count);
					foreach (Mono.Cecil.Cil.SequencePoint? point in methodDebugInfo.SequencePoints)
					{
						sequencePoints.Add(new SequencePoint {
							Offset = point.Offset,
							StartLine = point.StartLine,
							StartColumn = point.StartColumn,
							EndLine = point.EndLine,
							EndColumn = point.EndColumn,
							DocumentUrl = point.Document.Url
						});
					}
				}

				List<Variable> variables = new();
				foreach (ScopeDebugInformation? scope in methodDebugInfo.GetScopes())
				{
					if (!scope.HasVariables)
					{
						continue;
					}

					foreach (VariableDebugInformation? v in scope.Variables)
					{
						variables.Add(new Variable(v.Index, v.Name));
					}
				}

				this.debugInfo.Add(method, (sequencePoints, variables));
			}
		}

		public string Description { get; }

		public string SourceFileName { get; }

		public IList<SequencePoint> GetSequencePoints(SRM.MethodDefinitionHandle handle)
		{
			if (!debugInfo.TryGetValue(handle, out var info))
			{
				return EmptyList<SequencePoint>.Instance;
			}

			return info.SequencePoints;
		}

		public IList<Variable> GetVariables(SRM.MethodDefinitionHandle handle)
		{
			if (!debugInfo.TryGetValue(handle, out var info))
			{
				return EmptyList<Variable>.Instance;
			}

			return info.Variables;
		}

		public bool TryGetName(SRM.MethodDefinitionHandle handle, int index, [NotNullWhen(true)] out string? name)
		{
			name = null;
			if (!debugInfo.TryGetValue(handle, out var info))
			{
				return false;
			}

			var variable = info.Variables.FirstOrDefault(v => v.Index == index);
			name = variable.Name;
			return name != null;
		}
	}
}