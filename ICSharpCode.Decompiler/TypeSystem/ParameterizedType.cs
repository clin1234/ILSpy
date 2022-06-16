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

using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// ParameterizedType represents an instance of a generic type.
	/// Example: List&lt;string&gt;
	/// </summary>
	/// <remarks>
	/// When getting the members, this type modifies the lists so that
	/// type parameters in the signatures of the members are replaced with
	/// the type arguments.
	/// </remarks>
	[Serializable]
	public sealed class ParameterizedType : IType
	{
		readonly IType[] typeArguments;

		public ParameterizedType(IType genericType, IEnumerable<IType>? typeArguments)
		{
			ArgumentNullException.ThrowIfNull(typeArguments);
			this.GenericType = genericType ?? throw new ArgumentNullException(nameof(genericType));
			this.typeArguments = typeArguments.ToArray(); // copy input array to ensure it isn't modified
			if (this.typeArguments.Length == 0)
				throw new ArgumentException("Cannot use ParameterizedType with 0 type arguments.");
			if (genericType.TypeParameterCount != this.typeArguments.Length)
				throw new ArgumentException(
					"Number of type arguments must match the type definition's number of type parameters");
			ICompilationProvider? gp = genericType as ICompilationProvider;
			for (int i = 0; i < this.typeArguments.Length; i++)
			{
				switch (this.typeArguments[i])
				{
					case null:
						throw new ArgumentNullException("typeArguments[" + i + "]");
					case ICompilationProvider p when gp != null && p.Compilation != gp.Compilation:
						throw new InvalidOperationException(
							"Cannot parameterize a type with type arguments from a different compilation.");
				}
			}
		}

		/// <summary>
		/// Fast internal version of the constructor. (no safety checks)
		/// Keeps the array that was passed and assumes it won't be modified.
		/// </summary>
		internal ParameterizedType(IType genericType, params IType[] typeArguments)
		{
			Debug.Assert(genericType.TypeParameterCount == typeArguments.Length);
			this.GenericType = genericType;
			this.typeArguments = typeArguments;
		}

		public IType GenericType { get; }

		public TypeKind Kind {
			get { return GenericType.Kind; }
		}

		public bool? IsReferenceType => GenericType.IsReferenceType;
		public bool IsByRefLike => GenericType.IsByRefLike;
		public Nullability Nullability => GenericType.Nullability;

		public IType ChangeNullability(Nullability nullability)
		{
			IType newGenericType = GenericType.ChangeNullability(nullability);
			if (newGenericType == GenericType)
				return this;
			return new ParameterizedType(newGenericType, typeArguments);
		}

		public IType? DeclaringType {
			get {
				IType? declaringType = GenericType.DeclaringType;
				if (declaringType is { TypeParameterCount: > 0 } &&
				    declaringType.TypeParameterCount <= GenericType.TypeParameterCount)
				{
					IType[] newTypeArgs = new IType[declaringType.TypeParameterCount];
					Array.Copy(this.typeArguments, 0, newTypeArgs, 0, newTypeArgs.Length);
					return new ParameterizedType(declaringType, newTypeArgs);
				}

				return declaringType;
			}
		}

		public int TypeParameterCount {
			get { return typeArguments.Length; }
		}

		public string FullName {
			get { return GenericType.FullName; }
		}

		public string Name {
			get { return GenericType.Name; }
		}

		public string Namespace {
			get { return GenericType.Namespace; }
		}

		public string ReflectionName {
			get {
				StringBuilder b = new(GenericType.ReflectionName);
				b.Append('[');
				for (int i = 0; i < typeArguments.Length; i++)
				{
					if (i > 0)
						b.Append(',');
					b.Append('[');
					b.Append(typeArguments[i].ReflectionName);
					b.Append(']');
				}

				b.Append(']');
				return b.ToString();
			}
		}

		public IReadOnlyList<IType>? TypeArguments => typeArguments;

		public IReadOnlyList<ITypeParameter>? TypeParameters => GenericType.TypeParameters;

		/// <summary>
		/// Gets the definition of the generic type.
		/// For <c>ParameterizedType</c>, this method never returns null.
		/// </summary>
		public ITypeDefinition? GetDefinition()
		{
			return GenericType.GetDefinition();
		}

		/// <summary>
		/// Gets a type visitor that performs the substitution of class type parameters with the type arguments
		/// of this parameterized type.
		/// </summary>
		public TypeParameterSubstitution? GetSubstitution()
		{
			return new TypeParameterSubstitution(typeArguments, null);
		}

		public IEnumerable<IType> DirectBaseTypes {
			get {
				var substitution = GetSubstitution();
				return GenericType.DirectBaseTypes.Select(t => t.AcceptVisitor(substitution));
			}
		}

		public IEnumerable<IType> GetNestedTypes(Predicate<ITypeDefinition>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetNestedTypes(filter, options);
			return GetMembersHelper.GetNestedTypes(this, filter, options);
		}

		public IEnumerable<IType> GetNestedTypes(IReadOnlyList<IType?>? typeArguments,
			Predicate<ITypeDefinition>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetNestedTypes(typeArguments, filter, options);
			return GetMembersHelper.GetNestedTypes(this, typeArguments, filter, options);
		}

		public IEnumerable<IMethod> GetConstructors(Predicate<IMethod>? filter = null,
			GetMemberOptions options = GetMemberOptions.IgnoreInheritedMembers)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetConstructors(filter, options);
			return GetMembersHelper.GetConstructors(this, filter, options);
		}

		public IEnumerable<IMethod> GetMethods(Predicate<IMethod>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetMethods(filter, options);
			return GetMembersHelper.GetMethods(this, filter, options);
		}

		public IEnumerable<IMethod> GetMethods(IReadOnlyList<IType>? typeArguments, Predicate<IMethod>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetMethods(typeArguments, filter, options);
			return GetMembersHelper.GetMethods(this, typeArguments, filter, options);
		}

		public IEnumerable<IProperty> GetProperties(Predicate<IProperty>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetProperties(filter, options);
			return GetMembersHelper.GetProperties(this, filter, options);
		}

		public IEnumerable<IField> GetFields(Predicate<IField>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetFields(filter, options);
			return GetMembersHelper.GetFields(this, filter, options);
		}

		public IEnumerable<IEvent> GetEvents(Predicate<IEvent>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetEvents(filter, options);
			return GetMembersHelper.GetEvents(this, filter, options);
		}

		public IEnumerable<IMember> GetMembers(Predicate<IMember>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetMembers(filter, options);
			return GetMembersHelper.GetMembers(this, filter, options);
		}

		public IEnumerable<IMethod> GetAccessors(Predicate<IMethod>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			if ((options & GetMemberOptions.ReturnMemberDefinitions) == GetMemberOptions.ReturnMemberDefinitions)
				return GenericType.GetAccessors(filter, options);
			return GetMembersHelper.GetAccessors(this, filter, options);
		}

		public bool Equals(IType? other)
		{
			if (this == other)
				return true;
			if (other is not ParameterizedType c || !GenericType.Equals(c.GenericType) ||
			    typeArguments.Length != c.typeArguments.Length)
				return false;
			for (int i = 0; i < typeArguments.Length; i++)
			{
				if (!typeArguments[i].Equals(c.typeArguments[i]))
					return false;
			}

			return true;
		}

		public IType AcceptVisitor(TypeVisitor? visitor)
		{
			return visitor.VisitParameterizedType(this);
		}

		public IType VisitChildren(TypeVisitor? visitor)
		{
			IType g = GenericType.AcceptVisitor(visitor);
			// Keep ta == null as long as no elements changed, allocate the array only if necessary.
			IType[]? ta = (g != GenericType) ? new IType[typeArguments.Length] : null;
			for (int i = 0; i < typeArguments.Length; i++)
			{
				IType r = typeArguments[i].AcceptVisitor(visitor);
				if (r == null)
					throw new NullReferenceException("TypeVisitor.Visit-method returned null");
				if (ta == null && r != typeArguments[i])
				{
					// we found a difference, so we need to allocate the array
					ta = new IType[typeArguments.Length];
					for (int j = 0; j < i; j++)
					{
						ta[j] = typeArguments[j];
					}
				}

				if (ta != null)
					ta[i] = r;
			}

			if (ta == null)
				return this;
			return new ParameterizedType(g, ta);
		}

		public override string ToString()
		{
			StringBuilder b = new(GenericType.ToString());
			b.Append('[');
			for (int i = 0; i < typeArguments.Length; i++)
			{
				if (i > 0)
					b.Append(',');
				b.Append('[');
				b.Append(typeArguments[i]);
				b.Append(']');
			}

			b.Append(']');
			return b.ToString();
		}

		/// <summary>
		/// Same as 'parameterizedType.TypeArguments[index]'.
		/// </summary>
		public IType GetTypeArgument(int index)
		{
			return typeArguments[index];
		}

		/// <summary>
		/// Gets a type visitor that performs the substitution of class type parameters with the type arguments
		/// of this parameterized type,
		/// and also substitutes method type parameters with the specified method type arguments.
		/// </summary>
		public TypeParameterSubstitution? GetSubstitution(IReadOnlyList<IType>? methodTypeArguments)
		{
			return new TypeParameterSubstitution(typeArguments, methodTypeArguments);
		}

		public override bool Equals(object? obj)
		{
			return Equals(obj as IType);
		}

		public override int GetHashCode()
		{
			int hashCode = GenericType.GetHashCode();
			unchecked
			{
				foreach (var ta in typeArguments)
				{
					hashCode *= 1000000007;
					hashCode += 1000000009 * ta.GetHashCode();
				}
			}

			return hashCode;
		}
	}

	/// <summary>
	/// ParameterizedTypeReference is a reference to generic class that specifies the type parameters.
	/// Example: List&lt;string&gt;
	/// </summary>
	[Serializable]
	public sealed class ParameterizedTypeReference : ITypeReference, ISupportsInterning
	{
		readonly ITypeReference[] typeArguments;

		public ParameterizedTypeReference(ITypeReference genericType, IEnumerable<ITypeReference> typeArguments)
		{
			ArgumentNullException.ThrowIfNull(typeArguments);
			this.GenericType = genericType ?? throw new ArgumentNullException(nameof(genericType));
			this.typeArguments = typeArguments.ToArray();
			for (int i = 0; i < this.typeArguments.Length; i++)
			{
				if (this.typeArguments[i] == null)
					throw new ArgumentNullException("typeArguments[" + i + "]");
			}
		}

		public ITypeReference GenericType { get; }

		public IReadOnlyList<ITypeReference> TypeArguments {
			get {
				return typeArguments;
			}
		}

		public IType Resolve(ITypeResolveContext context)
		{
			IType baseType = GenericType.Resolve(context);
			int tpc = baseType.TypeParameterCount;
			if (tpc == 0)
				return baseType;
			IType[] resolvedTypes = new IType[tpc];
			for (int i = 0; i < resolvedTypes.Length; i++)
			{
				if (i < typeArguments.Length)
					resolvedTypes[i] = typeArguments[i].Resolve(context);
				else
					resolvedTypes[i] = SpecialType.UnknownType;
			}

			return new ParameterizedType(baseType, resolvedTypes);
		}

		public override string ToString()
		{
			StringBuilder b = new(GenericType.ToString());
			b.Append('[');
			for (int i = 0; i < typeArguments.Length; i++)
			{
				if (i > 0)
					b.Append(',');
				b.Append('[');
				b.Append(typeArguments[i]);
				b.Append(']');
			}

			b.Append(']');
			return b.ToString();
		}

		public int GetHashCodeForInterning()
		{
			int hashCode = GenericType.GetHashCode();
			unchecked
			{
				foreach (ITypeReference t in typeArguments)
				{
					hashCode *= 27;
					hashCode += t.GetHashCode();
				}
			}

			return hashCode;
		}

		public bool EqualsForInterning(ISupportsInterning other)
		{
			if (other is ParameterizedTypeReference o && GenericType == o.GenericType &&
			    typeArguments.Length == o.typeArguments.Length)
			{
				for (int i = 0; i < typeArguments.Length; i++)
				{
					if (typeArguments[i] != o.typeArguments[i])
						return false;
				}

				return true;
			}

			return false;
		}
	}
}