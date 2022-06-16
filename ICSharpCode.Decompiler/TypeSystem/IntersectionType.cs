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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;

using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.TypeSystem
{
	/// <summary>
	/// Represents the intersection of several types.
	/// </summary>
	internal sealed class IntersectionType : AbstractType
	{
		private IntersectionType(IType[] types)
		{
			Debug.Assert(types.Length >= 2);
			this.Types = Array.AsReadOnly(types);
		}

		private ReadOnlyCollection<IType> Types { get; }

		public override TypeKind Kind {
			get { return TypeKind.Intersection; }
		}

		public override string Name {
			get {
				StringBuilder b = new();
				foreach (var t in Types)
				{
					if (b.Length > 0)
						b.Append(" & ");
					b.Append(t.Name);
				}

				return b.ToString();
			}
		}

		public override string ReflectionName {
			get {
				StringBuilder b = new();
				foreach (var t in Types)
				{
					if (b.Length > 0)
						b.Append(" & ");
					b.Append(t.ReflectionName);
				}

				return b.ToString();
			}
		}

		public override bool? IsReferenceType {
			get {
				foreach (var t in Types)
				{
					bool? isReferenceType = t.IsReferenceType;
					if (isReferenceType.HasValue)
						return isReferenceType.Value;
				}

				return null;
			}
		}

		public override IEnumerable<IType> DirectBaseTypes {
			get { return Types; }
		}

		public static IType Create(IEnumerable<IType> types)
		{
			IType[] arr = types.Distinct().ToArray();
			foreach (IType? type in arr)
			{
				if (type == null)
					throw new ArgumentNullException();
			}

			return arr.Length switch {
				0 => SpecialType.UnknownType,
				1 => arr[0],
				_ => new IntersectionType(arr)
			};
		}

		public override IEnumerable<IType> DirectBaseTypes {
			get { return Types; }
		}

		public static IType Create(IEnumerable<IType> types)
		{
			IType[] arr = types.Distinct().ToArray();
			foreach (IType? type in arr)
			{
				if (type == null)
					throw new ArgumentNullException();
			}

			return arr.Length switch {
				0 => SpecialType.UnknownType,
				1 => arr[0],
				_ => new IntersectionType(arr)
			};
		}

		public override int GetHashCode()
		{
			int hashCode = 0;
			unchecked
			{
				foreach (var t in Types)
				{
					hashCode *= 7137517;
					hashCode += t.GetHashCode();
				}
			}

			return hashCode;
		}

		public override bool Equals(IType other)
		{
			if (other is IntersectionType o && Types.Count == o.Types.Count)
			{
				for (int i = 0; i < Types.Count; i++)
				{
					if (!Types[i].Equals(o.Types[i]))
						return false;
				}

				return true;
			}

			return false;
		}

		public override IEnumerable<IMethod> GetMethods(Predicate<IMethod>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetMethods(this, FilterNonStatic(filter), options);
		}

		public override IEnumerable<IMethod> GetMethods(IReadOnlyList<IType>? typeArguments, Predicate<IMethod>? filter = null,
			GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetMethods(this, typeArguments, filter, options);
		}

		public override IEnumerable<IProperty> GetProperties(Predicate<IProperty>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetProperties(this, FilterNonStatic(filter), options);
		}

		public override IEnumerable<IField> GetFields(Predicate<IField>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetFields(this, FilterNonStatic(filter), options);
		}

		public override IEnumerable<IEvent> GetEvents(Predicate<IEvent>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetEvents(this, FilterNonStatic(filter), options);
		}

		public override IEnumerable<IMember> GetMembers(Predicate<IMember>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetMembers(this, FilterNonStatic(filter), options);
		}

		public override IEnumerable<IMethod> GetAccessors(Predicate<IMethod>? filter = null, GetMemberOptions options = GetMemberOptions.None)
		{
			return GetMembersHelper.GetAccessors(this, FilterNonStatic(filter), options);
		}

		static Predicate<T> FilterNonStatic<T>(Predicate<T>? filter) where T : class, IMember
		{
			if (filter == null)
				return static member => !member.IsStatic;
			return member => !member.IsStatic && filter(member);
		}
	}
}