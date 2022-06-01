// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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
using System.Text;

using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// Default implementation of <see cref="IParameter"/>.
	/// </summary>
	internal sealed class DefaultParameter : IParameter
	{
		readonly IReadOnlyList<IAttribute> attributes;
		readonly object defaultValue;

		internal DefaultParameter(IType type, string name)
		{
			this.Type = type ?? throw new ArgumentNullException(nameof(type));
			this.Name = name ?? throw new ArgumentNullException(nameof(name));
			this.attributes = EmptyList<IAttribute>.Instance;
		}

		internal DefaultParameter(IType type, string name, IParameterizedMember owner = null,
			IReadOnlyList<IAttribute> attributes = null,
			ReferenceKind referenceKind = ReferenceKind.None, bool isParams = false, bool isOptional = false,
			object defaultValue = null)
		{
			this.Type = type ?? throw new ArgumentNullException(nameof(type));
			this.Name = name ?? throw new ArgumentNullException(nameof(name));
			this.Owner = owner;
			this.attributes = attributes ?? EmptyList<IAttribute>.Instance;
			this.ReferenceKind = referenceKind;
			this.IsParams = isParams;
			this.IsOptional = isOptional;
			this.defaultValue = defaultValue;
		}

		SymbolKind ISymbol.SymbolKind {
			get { return SymbolKind.Parameter; }
		}

		public IParameterizedMember Owner { get; }

		public IEnumerable<IAttribute?> GetAttributes() => attributes;

		public ReferenceKind ReferenceKind { get; }

		public bool IsRef => ReferenceKind == ReferenceKind.Ref;
		public bool IsOut => ReferenceKind == ReferenceKind.Out;
		public bool IsIn => ReferenceKind == ReferenceKind.In;

		public bool IsParams { get; }

		public bool IsOptional { get; }

		public string Name { get; }

		public IType Type { get; }

		bool IVariable.IsConst {
			get { return false; }
		}

		public bool HasConstantValueInSignature {
			get { return IsOptional; }
		}

		public object GetConstantValue(bool throwOnInvalidMetadata)
		{
			return defaultValue;
		}

		public override string ToString()
		{
			return ToString(this);
		}

		internal static string ToString(IParameter parameter)
		{
			StringBuilder b = new();
			if (parameter.IsRef)
				b.Append("ref ");
			if (parameter.IsOut)
				b.Append("out ");
			if (parameter.IsIn)
				b.Append("in ");
			if (parameter.IsParams)
				b.Append("params ");
			b.Append(parameter.Name);
			b.Append(':');
			b.Append(parameter.Type.ReflectionName);
			if (parameter.IsOptional && parameter.HasConstantValueInSignature)
			{
				b.Append(" = ");
				object val = parameter.GetConstantValue(throwOnInvalidMetadata: false);
				if (val != null)
					b.Append(val);
				else
					b.Append("null");
			}

			return b.ToString();
		}
	}
}