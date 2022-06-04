// Copyright (c) 2016 Daniel Grunwald
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
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;

using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Used when calling a vararg method. Stores the actual parameter types being passed.
	/// </summary>
	public class VarArgInstanceMethod : IMethod
	{
		readonly IParameter[] parameters;

		public VarArgInstanceMethod(IMethod baseMethod, IEnumerable<IType> varArgTypes)
		{
			this.BaseMethod = baseMethod;
			var paramList = new List<IParameter>(baseMethod.Parameters);
			Debug.Assert(paramList.Last().Type.Kind == TypeKind.ArgList);
			paramList.RemoveAt(paramList.Count - 1);
			foreach (IType varArg in varArgTypes)
			{
				paramList.Add(new DefaultParameter(varArg, name: string.Empty, owner: this));
			}

			this.parameters = paramList.ToArray();
		}

		public IMethod BaseMethod { get; }

		public int RegularParameterCount {
			get { return BaseMethod.Parameters.Count - 1; }
		}

		public IReadOnlyList<IParameter> Parameters {
			get { return parameters; }
		}

		public bool Equals(IMember obj, TypeVisitor typeNormalization)
		{
			return obj is VarArgInstanceMethod other && BaseMethod.Equals(other.BaseMethod, typeNormalization);
		}

		#region IHasAccessibility implementation

		public Accessibility Accessibility {
			get { return BaseMethod.Accessibility; }
		}

		#endregion

		#region ICompilationProvider implementation

		public ICompilation Compilation {
			get { return BaseMethod.Compilation; }
		}

		#endregion

		public override bool Equals(object obj)
		{
			return obj is VarArgInstanceMethod other && BaseMethod.Equals(other.BaseMethod);
		}

		public override int GetHashCode()
		{
			return BaseMethod.GetHashCode();
		}

		public override string ToString()
		{
			StringBuilder b = new("[");
			b.Append(this.SymbolKind);
			if (this.DeclaringType != null)
			{
				b.Append(this.DeclaringType.ReflectionName);
				b.Append('.');
			}

			b.Append(this.Name);
			if (this.TypeParameters.Count > 0)
			{
				b.Append("``");
				b.Append(this.TypeParameters.Count);
			}

			b.Append('(');
			for (int i = 0; i < this.Parameters.Count; i++)
			{
				if (i > 0)
					b.Append(", ");
				if (i == this.RegularParameterCount)
					b.Append("..., ");
				b.Append(this.Parameters[i].Type.ReflectionName);
			}

			if (this.Parameters.Count == this.RegularParameterCount)
			{
				b.Append(", ...");
			}

			b.Append("):");
			b.Append(this.ReturnType.ReflectionName);
			b.Append(']');
			return b.ToString();
		}

		#region IMethod implementation

		public IMethod Specialize(TypeParameterSubstitution substitution)
		{
			return new VarArgInstanceMethod(
				BaseMethod.Specialize(substitution),
				parameters.Skip(BaseMethod.Parameters.Count - 1).Select(p => p.Type.AcceptVisitor(substitution))
					.ToList());
		}

		IEnumerable<IAttribute> IEntity.GetAttributes() => BaseMethod.GetAttributes();
		IEnumerable<IAttribute> IMethod.GetReturnTypeAttributes() => BaseMethod.GetReturnTypeAttributes();
		bool IMethod.ReturnTypeIsRefReadOnly => BaseMethod.ReturnTypeIsRefReadOnly;
		bool IMethod.ThisIsRefReadOnly => BaseMethod.ThisIsRefReadOnly;
		bool IMethod.IsInitOnly => BaseMethod.IsInitOnly;

		public IReadOnlyList<ITypeParameter> TypeParameters {
			get { return BaseMethod.TypeParameters; }
		}

		public IReadOnlyList<IType> TypeArguments {
			get { return BaseMethod.TypeArguments; }
		}

		public System.Reflection.Metadata.EntityHandle MetadataToken => BaseMethod.MetadataToken;

		public bool IsExtensionMethod {
			get { return BaseMethod.IsExtensionMethod; }
		}

		bool IMethod.IsLocalFunction {
			get { return BaseMethod.IsLocalFunction; }
		}

		public bool IsConstructor {
			get { return BaseMethod.IsConstructor; }
		}

		public bool IsDestructor {
			get { return BaseMethod.IsDestructor; }
		}

		public bool IsOperator {
			get { return BaseMethod.IsOperator; }
		}

		public bool HasBody {
			get { return BaseMethod.HasBody; }
		}

		public bool IsAccessor => BaseMethod.IsAccessor;
		public IMember AccessorOwner => BaseMethod.AccessorOwner;
		public MethodSemanticsAttributes AccessorKind => BaseMethod.AccessorKind;

		public IMethod ReducedFrom {
			get { return BaseMethod.ReducedFrom; }
		}

		#endregion

		#region IMember implementation

		IMember IMember.Specialize(TypeParameterSubstitution substitution)
		{
			return Specialize(substitution);
		}

		public IMember MemberDefinition {
			get { return BaseMethod.MemberDefinition; }
		}

		public IType ReturnType {
			get { return BaseMethod.ReturnType; }
		}

		public IEnumerable<IMember> ExplicitlyImplementedInterfaceMembers {
			get { return BaseMethod.ExplicitlyImplementedInterfaceMembers; }
		}

		public bool IsExplicitInterfaceImplementation {
			get { return BaseMethod.IsExplicitInterfaceImplementation; }
		}

		public bool IsVirtual {
			get { return BaseMethod.IsVirtual; }
		}

		public bool IsOverride {
			get { return BaseMethod.IsOverride; }
		}

		public bool IsOverridable {
			get { return BaseMethod.IsOverridable; }
		}

		public TypeParameterSubstitution Substitution {
			get { return BaseMethod.Substitution; }
		}

		#endregion

		#region ISymbol implementation

		public SymbolKind SymbolKind {
			get { return BaseMethod.SymbolKind; }
		}

		public string Name {
			get { return BaseMethod.Name; }
		}

		#endregion

		#region IEntity implementation

		public ITypeDefinition DeclaringTypeDefinition {
			get { return BaseMethod.DeclaringTypeDefinition; }
		}

		public IType DeclaringType {
			get { return BaseMethod.DeclaringType; }
		}

		public IModule ParentModule {
			get { return BaseMethod.ParentModule; }
		}

		public bool IsStatic {
			get { return BaseMethod.IsStatic; }
		}

		public bool IsAbstract {
			get { return BaseMethod.IsAbstract; }
		}

		public bool IsSealed {
			get { return BaseMethod.IsSealed; }
		}

		#endregion

		#region INamedElement implementation

		public string FullName {
			get { return BaseMethod.FullName; }
		}

		public string ReflectionName {
			get { return BaseMethod.ReflectionName; }
		}

		public string Namespace {
			get { return BaseMethod.Namespace; }
		}

		#endregion
	}
}