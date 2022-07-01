﻿using System.Collections.Generic;

using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.CSharp.Transforms;
using ICSharpCode.Decompiler.TypeSystem;

namespace ICSharpCode.Decompiler.Tests.Helpers
{
	class RemoveCompilerAttribute : DepthFirstAstVisitor, IAstTransform
	{
		public override void VisitAttribute(Attribute attribute)
		{
			var section = (AttributeSection)attribute.Parent;
			SimpleType type = attribute.Type as SimpleType;
			if (section.AttributeTarget == "assembly" &&
				type.Identifier is "CompilationRelaxations" or "RuntimeCompatibility" or "SecurityPermission" or "PermissionSet" or "AssemblyVersion" or "Debuggable" or "TargetFramework")
			{
				attribute.Remove();
				if (section.Attributes.Count == 0)
					section.Remove();
			}
			if (section.AttributeTarget == "module" && type.Identifier == "UnverifiableCode")
			{
				attribute.Remove();
				if (section.Attributes.Count == 0)
					section.Remove();
			}
		}

		public void Run(AstNode rootNode, TransformContext context)
		{
			rootNode.AcceptVisitor(this);
		}
	}

	public class RemoveEmbeddedAttributes : DepthFirstAstVisitor, IAstTransform
	{
		HashSet<string> attributeNames = new() {
			"System.Runtime.CompilerServices.IsReadOnlyAttribute",
			"System.Runtime.CompilerServices.IsByRefLikeAttribute",
			"System.Runtime.CompilerServices.IsUnmanagedAttribute",
			"System.Runtime.CompilerServices.NullableAttribute",
			"System.Runtime.CompilerServices.NullableContextAttribute",
			"System.Runtime.CompilerServices.NativeIntegerAttribute",
			"Microsoft.CodeAnalysis.EmbeddedAttribute",
		};

		public override void VisitTypeDeclaration(TypeDeclaration typeDeclaration)
		{
			if (typeDeclaration.GetSymbol() is not ITypeDefinition typeDefinition || !attributeNames.Contains(typeDefinition.FullName))
				return;
			if (typeDeclaration.Parent is NamespaceDeclaration ns && ns.Members.Count == 1)
				ns.Remove();
			else
				typeDeclaration.Remove();
		}

		public void Run(AstNode rootNode, TransformContext context)
		{
			rootNode.AcceptVisitor(this);
		}
	}

	public class RemoveNamespaceMy : DepthFirstAstVisitor, IAstTransform
	{
		public override void VisitNamespaceDeclaration(NamespaceDeclaration namespaceDeclaration)
		{
			if (namespaceDeclaration.Name == "My")
			{
				namespaceDeclaration.Remove();
			}
			else
			{
				base.VisitNamespaceDeclaration(namespaceDeclaration);
			}
		}

		public void Run(AstNode rootNode, TransformContext context)
		{
			rootNode.AcceptVisitor(this);
		}
	}
}
