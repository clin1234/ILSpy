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

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Default ITypeResolveContext implementation.
	/// </summary>
	public sealed class SimpleTypeResolveContext : ITypeResolveContext
	{
		public SimpleTypeResolveContext(ICompilation compilation)
		{
			this.Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
		}

		public SimpleTypeResolveContext(IModule? module)
		{
			ArgumentNullException.ThrowIfNull(module);
			this.Compilation = module.Compilation;
			this.CurrentModule = module;
		}

		public SimpleTypeResolveContext(IEntity entity)
		{
			ArgumentNullException.ThrowIfNull(entity);
			this.Compilation = entity.Compilation;
			this.CurrentModule = entity.ParentModule;
			this.CurrentTypeDefinition = (entity as ITypeDefinition) ?? entity.DeclaringTypeDefinition;
			this.CurrentMember = entity as IMember;
		}

		private SimpleTypeResolveContext(ICompilation compilation, IModule currentModule,
			ITypeDefinition currentTypeDefinition, IMember currentMember)
		{
			this.Compilation = compilation;
			this.CurrentModule = currentModule;
			this.CurrentTypeDefinition = currentTypeDefinition;
			this.CurrentMember = currentMember;
		}

		public ICompilation Compilation { get; }

		public IModule CurrentModule { get; }

		public ITypeDefinition CurrentTypeDefinition { get; }

		public IMember CurrentMember { get; }

		public ITypeResolveContext WithCurrentTypeDefinition(ITypeDefinition typeDefinition)
		{
			return new SimpleTypeResolveContext(Compilation, CurrentModule, typeDefinition, CurrentMember);
		}

		public ITypeResolveContext WithCurrentMember(IMember? member)
		{
			return new SimpleTypeResolveContext(Compilation, CurrentModule, CurrentTypeDefinition, member);
		}
	}
}