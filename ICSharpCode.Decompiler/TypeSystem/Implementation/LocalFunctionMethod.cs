// Copyright (c) 2019 Siegfried Pammer
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
using System.Linq;
using System.Reflection;

using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// A local function has zero or more compiler-generated parameters added at the end.
	/// </summary>
	sealed class LocalFunctionMethod : IMethod
	{
		List<IParameter> parameters;

		List<IType> typeArguments;

		List<ITypeParameter> typeParameters;

		public LocalFunctionMethod(IMethod baseMethod, string name, bool isStaticLocalFunction,
			int numberOfCompilerGeneratedParameters, int numberOfCompilerGeneratedTypeParameters)
		{
			this.ReducedFrom = baseMethod ?? throw new ArgumentNullException(nameof(baseMethod));
			this.Name = name;
			this.IsStaticLocalFunction = isStaticLocalFunction;
			this.NumberOfCompilerGeneratedParameters = numberOfCompilerGeneratedParameters;
			this.NumberOfCompilerGeneratedTypeParameters = numberOfCompilerGeneratedTypeParameters;
		}

		internal int NumberOfCompilerGeneratedParameters { get; }

		internal int NumberOfCompilerGeneratedTypeParameters { get; }

		internal bool IsStaticLocalFunction { get; }

		public bool Equals(IMember obj, TypeVisitor typeNormalization)
		{
			if (obj is not LocalFunctionMethod other)
				return false;
			return ReducedFrom.Equals(other.ReducedFrom, typeNormalization)
			       && NumberOfCompilerGeneratedParameters == other.NumberOfCompilerGeneratedParameters
			       && NumberOfCompilerGeneratedTypeParameters == other.NumberOfCompilerGeneratedTypeParameters
			       && IsStaticLocalFunction == other.IsStaticLocalFunction;
		}

		public IMember MemberDefinition => this;

		public IType ReturnType => ReducedFrom.ReturnType;

		IEnumerable<IMember> IMember.ExplicitlyImplementedInterfaceMembers =>
			ReducedFrom.ExplicitlyImplementedInterfaceMembers;

		bool IMember.IsExplicitInterfaceImplementation => ReducedFrom.IsExplicitInterfaceImplementation;
		public bool IsVirtual => ReducedFrom.IsVirtual;
		public bool IsOverride => ReducedFrom.IsOverride;
		public bool IsOverridable => ReducedFrom.IsOverridable;
		public TypeParameterSubstitution Substitution => ReducedFrom.Substitution;

		public IMethod Specialize(TypeParameterSubstitution substitution)
		{
			return new LocalFunctionMethod(
				ReducedFrom.Specialize(substitution),
				Name, IsStaticLocalFunction, NumberOfCompilerGeneratedParameters,
				NumberOfCompilerGeneratedTypeParameters);
		}

		public IMember MemberDefinition => this;

		public IType ReturnType => ReducedFrom.ReturnType;

		IEnumerable<IMember> IMember.ExplicitlyImplementedInterfaceMembers =>
			ReducedFrom.ExplicitlyImplementedInterfaceMembers;

		bool IMember.IsExplicitInterfaceImplementation => ReducedFrom.IsExplicitInterfaceImplementation;
		public bool IsVirtual => ReducedFrom.IsVirtual;
		public bool IsOverride => ReducedFrom.IsOverride;
		public bool IsOverridable => ReducedFrom.IsOverridable;
		public TypeParameterSubstitution? Substitution => ReducedFrom.Substitution;

		public IMethod Specialize(TypeParameterSubstitution? substitution)
		{
			return new LocalFunctionMethod(
				ReducedFrom.Specialize(substitution),
				Name, IsStaticLocalFunction, NumberOfCompilerGeneratedParameters,
				NumberOfCompilerGeneratedTypeParameters);
		}

		IMember IMember.Specialize(TypeParameterSubstitution? substitution)
		{
			return Specialize(substitution);
		}

		public bool IsExtensionMethod => ReducedFrom.IsExtensionMethod;
		public bool IsLocalFunction => true;
		public bool IsConstructor => ReducedFrom.IsConstructor;
		public bool IsDestructor => ReducedFrom.IsDestructor;
		public bool IsOperator => ReducedFrom.IsOperator;
		public bool HasBody => ReducedFrom.HasBody;
		public bool IsAccessor => ReducedFrom.IsAccessor;
		public IMember AccessorOwner => ReducedFrom.AccessorOwner;
		public MethodSemanticsAttributes AccessorKind => ReducedFrom.AccessorKind;
		public IMethod ReducedFrom { get; }

		public IReadOnlyList<ITypeParameter> TypeParameters {
			get {
				return typeParameters ??=
					new List<ITypeParameter>(ReducedFrom.TypeParameters.Skip(NumberOfCompilerGeneratedTypeParameters));
			}
		}

		public IReadOnlyList<IType> TypeArguments {
			get {
				return typeArguments ??=
					new List<IType>(ReducedFrom.TypeArguments.Skip(NumberOfCompilerGeneratedTypeParameters));
			}
		}

		public IReadOnlyList<IParameter> Parameters {
			get {
				return parameters ??=
					new List<IParameter>(ReducedFrom.Parameters.SkipLast(NumberOfCompilerGeneratedParameters));
			}
		}

		public System.Reflection.Metadata.EntityHandle MetadataToken => ReducedFrom.MetadataToken;
		public SymbolKind SymbolKind => ReducedFrom.SymbolKind;
		public ITypeDefinition DeclaringTypeDefinition => ReducedFrom.DeclaringTypeDefinition;
		public IType DeclaringType => ReducedFrom.DeclaringType;
		public IModule ParentModule => ReducedFrom.ParentModule;
		IEnumerable<IAttribute> IEntity.GetAttributes() => ReducedFrom.GetAttributes();
		IEnumerable<IAttribute> IMethod.GetReturnTypeAttributes() => ReducedFrom.GetReturnTypeAttributes();
		bool IMethod.ReturnTypeIsRefReadOnly => ReducedFrom.ReturnTypeIsRefReadOnly;
		bool IMethod.ThisIsRefReadOnly => ReducedFrom.ThisIsRefReadOnly;
		bool IMethod.IsInitOnly => ReducedFrom.IsInitOnly;

		/// <summary>
		/// We consider local functions as always static, because they do not have a "this parameter".
		/// Even local functions in instance methods capture this.
		/// </summary>
		public bool IsStatic => true;

		public bool IsAbstract => ReducedFrom.IsAbstract;
		public bool IsSealed => ReducedFrom.IsSealed;

		public Accessibility Accessibility => ReducedFrom.Accessibility;

		public string FullName => Name;
		public string Name { get; set; }
		public string ReflectionName => ReducedFrom.ReflectionName;
		public string Namespace => ReducedFrom.Namespace;

		public ICompilation Compilation => ReducedFrom.Compilation;

		public override bool Equals(object obj)
		{
			if (obj is not LocalFunctionMethod other)
				return false;
			return ReducedFrom.Equals(other.ReducedFrom)
			       && NumberOfCompilerGeneratedParameters == other.NumberOfCompilerGeneratedParameters
			       && NumberOfCompilerGeneratedTypeParameters == other.NumberOfCompilerGeneratedTypeParameters
			       && IsStaticLocalFunction == other.IsStaticLocalFunction;
		}

		public override int GetHashCode()
		{
			return ReducedFrom.GetHashCode();
		}

		public override string ToString()
		{
			return
				$"[LocalFunctionMethod: ReducedFrom={ReducedFrom}, Name={Name}, NumberOfGeneratedParameters={NumberOfCompilerGeneratedParameters}, NumberOfCompilerGeneratedTypeParameters={NumberOfCompilerGeneratedTypeParameters}, IsStaticLocalFunction={IsStaticLocalFunction}]";
		}
	}
}