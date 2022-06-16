﻿// Copyright (c) 2018 Daniel Grunwald
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

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.Semantics
{
	/// <summary>
	/// Resolve result for a C# 7 tuple literal.
	/// </summary>
	public sealed class TupleResolveResult : ResolveResult
	{
		public TupleResolveResult(ICompilation compilation,
			ImmutableArray<ResolveResult> elements,
			ImmutableArray<string?> elementNames = default(ImmutableArray<string>),
			IModule? valueTupleAssembly = null)
			: base(GetTupleType(compilation, elements, elementNames, valueTupleAssembly))
		{
			this.Elements = elements;
		}

		public ImmutableArray<ResolveResult> Elements { get; }

		public override IEnumerable<ResolveResult> GetChildResults()
		{
			return Elements;
		}

		static IType GetTupleType(ICompilation compilation, ImmutableArray<ResolveResult> elements,
			ImmutableArray<string?> elementNames, IModule? valueTupleAssembly)
		{
			if (elements.Any(e => e.Type.Kind is TypeKind.None or TypeKind.Null))
				return SpecialType.NoType;
			return new TupleType(compilation, elements.Select(e => e.Type).ToImmutableArray(), elementNames,
				valueTupleAssembly);
		}
	}
}