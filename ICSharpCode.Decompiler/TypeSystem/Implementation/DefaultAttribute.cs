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
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// IAttribute implementation for already-resolved attributes.
	/// </summary>
	internal sealed class DefaultAttribute : IAttribute
	{
		volatile IMethod constructor;

		public DefaultAttribute(IType attributeType,
			ImmutableArray<CustomAttributeTypedArgument<IType>> fixedArguments,
			ImmutableArray<CustomAttributeNamedArgument<IType>> namedArguments)
		{
			this.AttributeType = attributeType ?? throw new ArgumentNullException(nameof(attributeType));
			this.FixedArguments = fixedArguments;
			this.NamedArguments = namedArguments;
		}

		public ImmutableArray<CustomAttributeTypedArgument<IType>> FixedArguments { get; }
		public ImmutableArray<CustomAttributeNamedArgument<IType>> NamedArguments { get; }

		public IType AttributeType { get; }

		bool IAttribute.HasDecodeErrors => false;

		public IMethod Constructor {
			get {
				IMethod ctor = this.constructor;
				if (ctor == null)
				{
					foreach (IMethod candidate in this.AttributeType.GetConstructors(m =>
						         m.Parameters.Count == FixedArguments.Length))
					{
						if (candidate.Parameters.Select(static p => p.Type)
						    .SequenceEqual(this.FixedArguments.Select(static a => a.Type)))
						{
							ctor = candidate;
							break;
						}
					}

					this.constructor = ctor;
				}

				return ctor;
			}
		}
	}
}