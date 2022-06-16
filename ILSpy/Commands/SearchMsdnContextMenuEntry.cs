﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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

using System.Linq;

using ICSharpCode.ILSpy.Properties;
using ICSharpCode.ILSpy.TreeNodes;
namespace ICSharpCode.ILSpy
{
	using Decompiler.TypeSystem;

	[ExportContextMenuEntry(Header = nameof(Resources.SearchMSDN), Icon = "images/SearchMsdn", Order = 9999)]
	internal sealed class SearchMsdnContextMenuEntry : IContextMenuEntry
	{
		private const string msdnAddress = "https://docs.microsoft.com/dotnet/api/{0}";

		public bool IsVisible(TextViewContext context)
		{
			if (context.SelectedTreeNodes == null)
				return false;

			return context.SelectedTreeNodes.All(static n => n is NamespaceTreeNode or TypeTreeNode or EventTreeNode or FieldTreeNode or PropertyTreeNode or MethodTreeNode);
		}

		public bool IsEnabled(TextViewContext context)
		{
			if (context.SelectedTreeNodes == null)
				return false;

			foreach (var node in context.SelectedTreeNodes)
			{
				switch (node)
				{
					case TypeTreeNode { IsPublicAPI: false }:
					case EventTreeNode eventNode when (!eventNode.IsPublicAPI || !IsAccessible(eventNode.EventDefinition)):
					case FieldTreeNode fieldNode when (!fieldNode.IsPublicAPI || !IsAccessible(fieldNode.FieldDefinition) || IsDelegateOrEnumMember(fieldNode.FieldDefinition)):
					case PropertyTreeNode propertyNode when (!propertyNode.IsPublicAPI || !IsAccessible(propertyNode.PropertyDefinition)):
					case MethodTreeNode methodNode when (!methodNode.IsPublicAPI || !IsAccessible(methodNode.MethodDefinition) || IsDelegateOrEnumMember(methodNode.MethodDefinition)):
					case NamespaceTreeNode namespaceNode when string.IsNullOrEmpty(namespaceNode.Name):
						return false;
				}
			}

			return true;
		}

		bool IsAccessible(IEntity entity)
		{
			if (entity.DeclaringTypeDefinition == null)
				return false;
			switch (entity.DeclaringTypeDefinition.Accessibility)
			{
				case Accessibility.Public:
				case Accessibility.Protected:
				case Accessibility.ProtectedOrInternal:
					return true;
				default:
					return false;
			}
		}

		bool IsDelegateOrEnumMember(IMember member)
		{
			if (member.DeclaringTypeDefinition == null)
				return false;
			switch (member.DeclaringTypeDefinition.Kind)
			{
				case TypeKind.Delegate:
				case TypeKind.Enum:
					return true;
				default:
					return false;
			}
		}

		public void Execute(TextViewContext context)
		{
			if (context.SelectedTreeNodes != null)
			{
				foreach (var node in context.SelectedTreeNodes.Cast<ILSpyTreeNode>())
				{
					SearchMsdn(node);
				}
			}
		}

		public static void SearchMsdn(ILSpyTreeNode node)
		{
			var address = string.Empty;

			switch (node)
			{
				case NamespaceTreeNode namespaceNode:
					address = string.Format(msdnAddress, namespaceNode.Name);
					break;
				case IMemberTreeNode memberNode:
				{
					var member = memberNode.Member;
					var memberName = member.ReflectionName.Replace('`', '-').Replace('+', '.');
					if (memberName.EndsWith("..ctor", System.StringComparison.Ordinal))
						memberName = memberName[..^5] + "-ctor";

					address = string.Format(msdnAddress, memberName);
					break;
				}
			}

			address = address.ToLower();
			if (!string.IsNullOrEmpty(address))
				MainWindow.OpenLink(address);
		}
	}
}