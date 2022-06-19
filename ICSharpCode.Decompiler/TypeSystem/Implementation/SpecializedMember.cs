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
using System.Diagnostics;
using System.Linq;
using System.Text;

using ICSharpCode.Decompiler.Util;

namespace ICSharpCode.Decompiler.TypeSystem.Implementation
{
	/// <summary>
	/// Represents a SpecializedMember (a member on which type substitution has been performed).
	/// </summary>
	public abstract class SpecializedMember : IMember
	{
		protected readonly IMember baseMember;

		IType declaringType;
		IType returnType;

		protected SpecializedMember(IMember memberDefinition)
		{
			if (memberDefinition == null) throw new ArgumentNullException(nameof(memberDefinition));
			if (memberDefinition is SpecializedMember)
				throw new ArgumentException(
					"Member definition cannot be specialized. Please use IMember.Specialize() instead of directly constructing SpecializedMember instances.");

			this.baseMember = memberDefinition;
			this.Substitution = TypeParameterSubstitution.Identity;
		}

		/// <summary>
		/// Gets the substitution belonging to this specialized member.
		/// </summary>
		public TypeParameterSubstitution? Substitution { get; private set; }

		public IType DeclaringType {
			get {
				var result = LazyInit.VolatileRead(ref this.declaringType);
				if (result != null)
					return result;
				IType definitionDeclaringType = baseMember.DeclaringType;
				if (definitionDeclaringType is ITypeDefinition definitionDeclaringTypeDef &&
				    definitionDeclaringType.TypeParameterCount > 0)
				{
					if (Substitution.ClassTypeArguments != null && Substitution.ClassTypeArguments.Count ==
					    definitionDeclaringType.TypeParameterCount)
					{
						result = new ParameterizedType(definitionDeclaringTypeDef, Substitution.ClassTypeArguments);
					}
					else
					{
						result = new ParameterizedType(definitionDeclaringTypeDef,
							definitionDeclaringTypeDef.TypeParameters).AcceptVisitor(Substitution);
					}
				}
				else if (definitionDeclaringType != null)
				{
					result = definitionDeclaringType.AcceptVisitor(Substitution);
				}

				return LazyInit.GetOrSet(ref this.declaringType, result);
			}
			internal init {
				// This setter is used as an optimization when the code constructing
				// the SpecializedMember already knows the declaring type.
				Debug.Assert(this.declaringType == null);
				// As this setter is used only during construction before the member is published
				// to other threads, we don't need a volatile write.
				this.declaringType = value;
			}
		}

		public IMember MemberDefinition {
			get { return baseMember.MemberDefinition; }
		}

		public IType ReturnType {
			get {
				var result = LazyInit.VolatileRead(ref this.returnType);
				if (result != null)
					return result;
				return LazyInit.GetOrSet(ref this.returnType, baseMember.ReturnType.AcceptVisitor(Substitution));
			}
			protected init {
				// This setter is used for LiftedUserDefinedOperator, a special case of specialized member
				// (not a normal type parameter substitution).

				// As this setter is used only during construction before the member is published
				// to other threads, we don't need a volatile write.
				this.returnType = value;
			}
		}

		public System.Reflection.Metadata.EntityHandle MetadataToken => baseMember.MetadataToken;

		public bool IsVirtual {
			get { return baseMember.IsVirtual; }
		}

		public bool IsOverride {
			get { return baseMember.IsOverride; }
		}

		public bool IsOverridable {
			get { return baseMember.IsOverridable; }
		}

		public SymbolKind SymbolKind {
			get { return baseMember.SymbolKind; }
		}

		public ITypeDefinition DeclaringTypeDefinition {
			get { return baseMember.DeclaringTypeDefinition; }
		}

		IEnumerable<IAttribute?> IEntity.GetAttributes() => baseMember.GetAttributes();

		public IEnumerable<IMember> ExplicitlyImplementedInterfaceMembers {
			get {
				// Note: if the interface is generic, then the interface members should already be specialized,
				// so we only need to append our substitution.
				return baseMember.ExplicitlyImplementedInterfaceMembers.Select(m => m.Specialize(Substitution));
			}
		}

		public bool IsExplicitInterfaceImplementation {
			get { return baseMember.IsExplicitInterfaceImplementation; }
		}

		public Accessibility Accessibility {
			get { return baseMember.Accessibility; }
		}

		public bool IsStatic {
			get { return baseMember.IsStatic; }
		}

		public bool IsAbstract {
			get { return baseMember.IsAbstract; }
		}

		public bool IsSealed {
			get { return baseMember.IsSealed; }
		}

		public string FullName {
			get { return baseMember.FullName; }
		}

		public string Name {
			get { return baseMember.Name; }
		}

		public string Namespace {
			get { return baseMember.Namespace; }
		}

		public string ReflectionName {
			get { return baseMember.ReflectionName; }
		}

		public ICompilation Compilation {
			get { return baseMember.Compilation; }
		}

		public IModule? ParentModule {
			get { return baseMember.ParentModule; }
		}

		public virtual IMember Specialize(TypeParameterSubstitution? newSubstitution)
		{
			return baseMember.Specialize(TypeParameterSubstitution.Compose(newSubstitution, this.Substitution));
		}

		public virtual bool Equals(IMember obj, TypeVisitor? typeNormalization)
		{
			if (obj is not SpecializedMember other)
				return false;
			return this.baseMember.Equals(other.baseMember, typeNormalization)
			       && this.Substitution.Equals(other.Substitution, typeNormalization);
		}

		/// <summary>
		/// Performs a substitution. This method may only be called by constructors in derived classes.
		/// </summary>
		protected void AddSubstitution(TypeParameterSubstitution? newSubstitution)
		{
			Debug.Assert(declaringType == null);
			Debug.Assert(returnType == null);
			this.Substitution = TypeParameterSubstitution.Compose(newSubstitution, this.Substitution);
		}

		internal IMethod WrapAccessor(ref IMethod? cachingField, IMethod accessorDefinition)
		{
			if (accessorDefinition == null)
				return null;
			var result = LazyInit.VolatileRead(ref cachingField);
			if (result != null)
			{
				return result;
			}

			var sm = accessorDefinition.Specialize(Substitution);
			//sm.AccessorOwner = this;
			return LazyInit.GetOrSet(ref cachingField, sm);
		}

		public override bool Equals(object obj)
		{
			if (obj is not SpecializedMember other)
				return false;
			return this.baseMember.Equals(other.baseMember) && this.Substitution.Equals(other.Substitution);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return 1000000007 * baseMember.GetHashCode() + 1000000009 * Substitution.GetHashCode();
			}
		}

		public override string ToString()
		{
			StringBuilder b = new("[");
			b.Append(GetType().Name);
			b.Append(' ');
			b.Append(this.DeclaringType);
			b.Append('.');
			b.Append(this.Name);
			b.Append(':');
			b.Append(this.ReturnType);
			b.Append(']');
			return b.ToString();
		}
	}

	public abstract class SpecializedParameterizedMember : SpecializedMember, IParameterizedMember
	{
		IReadOnlyList<IParameter?> parameters;

		protected SpecializedParameterizedMember(IParameterizedMember memberDefinition)
			: base(memberDefinition)
		{
		}

		public IReadOnlyList<IParameter?> Parameters {
			get {
				var result = LazyInit.VolatileRead(ref this.parameters);
				if (result != null)
					return result;
				return LazyInit.GetOrSet(ref this.parameters,
					CreateParameters(t => t.AcceptVisitor(this.Substitution)));
			}
			protected init {
				// This setter is used for LiftedUserDefinedOperator, a special case of specialized member
				// (not a normal type parameter substitution).

				// As this setter is used only during construction before the member is published
				// to other threads, we don't need a volatile write.
				this.parameters = value;
			}
		}

		protected IParameter?[] CreateParameters(Func<IType, IType> substitution)
		{
			var paramDefs = ((IParameterizedMember)this.baseMember).Parameters;
			if (paramDefs.Count == 0)
			{
				return Empty<IParameter>.Array;
			}

			var parameters = new IParameter?[paramDefs.Count];
			for (int i = 0; i < parameters.Length; i++)
			{
				var p = paramDefs[i];
				IType newType = substitution(p.Type);
				parameters[i] = new SpecializedParameter(p, newType, this);
			}

			return parameters;
		}

		public override string ToString()
		{
			StringBuilder b = new("[");
			b.Append(GetType().Name);
			b.Append(' ');
			b.Append(this.DeclaringType.ReflectionName);
			b.Append('.');
			b.Append(this.Name);
			b.Append('(');
			for (int i = 0; i < this.Parameters.Count; i++)
			{
				if (i > 0)
					b.Append(", ");
				b.Append(this.Parameters[i]);
			}

			b.Append("):");
			b.Append(this.ReturnType.ReflectionName);
			b.Append(']');
			return b.ToString();
		}
	}
}