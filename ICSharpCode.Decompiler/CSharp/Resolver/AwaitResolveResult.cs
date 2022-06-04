﻿// Copyright (c) 2010-2013 AlphaSierraPapa for the SharpDevelop Team
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

using ICSharpCode.Decompiler.Semantics;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.CSharp.Resolver
{
	/// <summary>
	/// Represents the result of an await expression.
	/// </summary>
	public sealed class AwaitResolveResult : ResolveResult
	{
		/// <summary>
		/// Awaiter type. Will not be null (but can be UnknownType).
		/// </summary>
		public readonly IType AwaiterType;

		/// <summary>
		/// The method representing the GetAwaiter() call. Can be an <see cref="InvocationResolveResult"/> or a <see cref="DynamicInvocationResolveResult"/>.
		/// </summary>
		public readonly ResolveResult GetAwaiterInvocation;

		/// <summary>
		/// Method representing the GetResult method on the awaiter type. Can be null if the awaiter type or the method was not found, or when awaiting a dynamic expression.
		/// </summary>
		public readonly IMethod GetResultMethod;

		/// <summary>
		/// Property representing the IsCompleted property on the awaiter type. Can be null if the awaiter type or the property was not found, or when awaiting a dynamic expression.
		/// </summary>
		public readonly IProperty IsCompletedProperty;

		/// <summary>
		/// Method representing the OnCompleted method on the awaiter type. Can be null if the awaiter type or the method was not found, or when awaiting a dynamic expression.
		/// This can also refer to an UnsafeOnCompleted method, if the awaiter type implements <c>System.Runtime.CompilerServices.ICriticalNotifyCompletion</c>.
		/// </summary>
		public readonly IMethod OnCompletedMethod;

		public AwaitResolveResult(IType resultType, ResolveResult getAwaiterInvocation, IType awaiterType,
			IProperty isCompletedProperty, IMethod onCompletedMethod, IMethod getResultMethod)
			: base(resultType)
		{
			this.GetAwaiterInvocation =
				getAwaiterInvocation ?? throw new ArgumentNullException(nameof(getAwaiterInvocation));
			this.AwaiterType = awaiterType ?? throw new ArgumentNullException(nameof(awaiterType));
			this.IsCompletedProperty = isCompletedProperty;
			this.OnCompletedMethod = onCompletedMethod;
			this.GetResultMethod = getResultMethod;
		}

		public override bool IsError {
			get {
				return this.GetAwaiterInvocation.IsError || (AwaiterType.Kind != TypeKind.Dynamic &&
				                                             (this.IsCompletedProperty == null ||
				                                              this.OnCompletedMethod == null ||
				                                              this.GetResultMethod == null));
			}
		}

		public override IEnumerable<ResolveResult> GetChildResults()
		{
			return new[] { GetAwaiterInvocation };
		}
	}
}