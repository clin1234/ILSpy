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
using System.IO;
using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Output;
using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.Decompiler.TypeSystem.Implementation;

namespace ICSharpCode.Decompiler.CSharp.OutputVisitor
{
	/// <summary>
	/// C# ambience. Used to convert type system symbols to text (usually for displaying the symbol to the user; e.g. in editor tooltips)
	/// </summary>
	internal sealed class CSharpAmbience : IAmbience
	{
		public ConversionFlags ConversionFlags { get; set; }

		public string ConvertType(IType? type)
		{
			ArgumentNullException.ThrowIfNull(type);

			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			astBuilder.AlwaysUseShortTypeNames = (ConversionFlags & ConversionFlags.UseFullyQualifiedEntityNames) !=
			                                     ConversionFlags.UseFullyQualifiedEntityNames;
			AstType? astType = astBuilder.ConvertType(type);
			return astType.ToString();
		}

		public string ConvertConstantValue(object constantValue)
		{
			return TextWriterTokenWriter.PrintPrimitiveValue(constantValue);
		}

		public string WrapComment(string comment)
		{
			return "// " + comment;
		}

		public string ConvertVariable(IVariable v)
		{
			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			AstNode? astNode = astBuilder.ConvertVariable(v);
			return astNode.ToString().TrimEnd(';', '\r', '\n', (char)8232);
		}

		private void ConvertType(IType type, TokenWriter writer, CSharpFormattingOptions formattingPolicy)
		{
			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			astBuilder.AlwaysUseShortTypeNames = (ConversionFlags & ConversionFlags.UseFullyQualifiedEntityNames) !=
			                                     ConversionFlags.UseFullyQualifiedEntityNames;
			AstType? astType = astBuilder.ConvertType(type);
			astType.AcceptVisitor(new CSharpOutputVisitor(writer, formattingPolicy));
		}

		#region ConvertSymbol

		public string ConvertSymbol(ISymbol symbol)
		{
			ArgumentNullException.ThrowIfNull(symbol);

			StringWriter writer = new();
			ConvertSymbol(symbol, new TextWriterTokenWriter(writer), FormattingOptionsFactory.CreateEmpty());
			return writer.ToString();
		}

		public void ConvertSymbol(ISymbol symbol, TokenWriter writer, CSharpFormattingOptions formattingPolicy)
		{
			ArgumentNullException.ThrowIfNull(symbol);
			ArgumentNullException.ThrowIfNull(writer);
			ArgumentNullException.ThrowIfNull(formattingPolicy);

			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			AstNode? node = astBuilder.ConvertSymbol(symbol);
			writer.StartNode(node);
			if (node is EntityDeclaration entityDecl)
				PrintModifiers(entityDecl.Modifiers, writer);

			if ((ConversionFlags & ConversionFlags.ShowDefinitionKeyword) == ConversionFlags.ShowDefinitionKeyword)
			{
				switch (node)
				{
					case TypeDeclaration declaration:
						switch (declaration.ClassType)
						{
							case ClassType.Class:
								writer.WriteKeyword(Roles.ClassKeyword, "class");
								break;
							case ClassType.Struct:
								writer.WriteKeyword(Roles.StructKeyword, "struct");
								break;
							case ClassType.Interface:
								writer.WriteKeyword(Roles.InterfaceKeyword, "interface");
								break;
							case ClassType.Enum:
								writer.WriteKeyword(Roles.EnumKeyword, "enum");
								break;
							case ClassType.RecordClass:
								writer.WriteKeyword(Roles.RecordKeyword, "record");
								break;
							default:
								throw new Exception("Invalid value for ClassType");
						}

						writer.Space();
						break;
					case DelegateDeclaration:
						writer.WriteKeyword(Roles.DelegateKeyword, "delegate");
						writer.Space();
						break;
					case EventDeclaration:
						writer.WriteKeyword(EventDeclaration.EventKeywordRole, "event");
						writer.Space();
						break;
					case NamespaceDeclaration:
						writer.WriteKeyword(Roles.NamespaceKeyword, "namespace");
						writer.Space();
						break;
				}
			}

			if ((ConversionFlags & ConversionFlags.PlaceReturnTypeAfterParameterList) !=
			    ConversionFlags.PlaceReturnTypeAfterParameterList
			    && (ConversionFlags & ConversionFlags.ShowReturnType) == ConversionFlags.ShowReturnType)
			{
				var rt = node.GetChildByRole(Roles.Type);
				if (!rt.IsNull)
				{
					rt.AcceptVisitor(new CSharpOutputVisitor(writer, formattingPolicy));
					writer.Space();
				}
			}

			switch (symbol)
			{
				case ITypeDefinition definition:
					WriteTypeDeclarationName(definition, writer, formattingPolicy);
					break;
				case IMember member:
					WriteMemberDeclarationName(member, writer, formattingPolicy);
					break;
				default:
					writer.WriteIdentifier(Identifier.Create(symbol.Name));
					break;
			}

			if ((ConversionFlags & ConversionFlags.ShowParameterList) == ConversionFlags.ShowParameterList &&
			    HasParameters(symbol))
			{
				writer.WriteToken(symbol.SymbolKind == SymbolKind.Indexer ? Roles.LBracket : Roles.LPar,
					symbol.SymbolKind == SymbolKind.Indexer ? "[" : "(");
				bool first = true;
				foreach (var param in node.GetChildrenByRole(Roles.Parameter))
				{
					if ((ConversionFlags & ConversionFlags.ShowParameterModifiers) == 0)
					{
						param.ParameterModifier = ParameterModifier.None;
					}

					if ((ConversionFlags & ConversionFlags.ShowParameterDefaultValues) == 0)
					{
						param.DefaultExpression.Detach();
					}

					if (first)
					{
						first = false;
					}
					else
					{
						writer.WriteToken(Roles.Comma, ",");
						writer.Space();
					}

					param.AcceptVisitor(new CSharpOutputVisitor(writer, formattingPolicy));
				}

				writer.WriteToken(symbol.SymbolKind == SymbolKind.Indexer ? Roles.RBracket : Roles.RPar,
					symbol.SymbolKind == SymbolKind.Indexer ? "]" : ")");
			}

			if ((ConversionFlags & ConversionFlags.PlaceReturnTypeAfterParameterList) ==
			    ConversionFlags.PlaceReturnTypeAfterParameterList
			    && (ConversionFlags & ConversionFlags.ShowReturnType) == ConversionFlags.ShowReturnType)
			{
				var rt = node.GetChildByRole(Roles.Type);
				if (!rt.IsNull)
				{
					writer.Space();
					writer.WriteToken(Roles.Colon, ":");
					writer.Space();
					if (symbol is IField f && CSharpDecompiler.IsFixedField(f, out var type, out int elementCount))
					{
						rt = astBuilder.ConvertType(type);
						new IndexerExpression(new TypeReferenceExpression(rt),
								astBuilder.ConvertConstantValue(f.Compilation.FindType(KnownTypeCode.Int32),
									elementCount))
							.AcceptVisitor(new CSharpOutputVisitor(writer, formattingPolicy));
					}
					else
					{
						rt.AcceptVisitor(new CSharpOutputVisitor(writer, formattingPolicy));
					}
				}
			}

			if ((ConversionFlags & ConversionFlags.ShowBody) == ConversionFlags.ShowBody && node is not TypeDeclaration)
			{
				if (symbol is IProperty property)
				{
					writer.Space();
					writer.WriteToken(Roles.LBrace, "{");
					writer.Space();
					if (property.CanGet)
					{
						writer.WriteKeyword(PropertyDeclaration.GetKeywordRole, "get");
						writer.WriteToken(Roles.Semicolon, ";");
						writer.Space();
					}

					if (property.CanSet)
					{
						writer.WriteKeyword(PropertyDeclaration.SetKeywordRole, "set");
						writer.WriteToken(Roles.Semicolon, ";");
						writer.Space();
					}

					writer.WriteToken(Roles.RBrace, "}");
				}
				else
				{
					writer.WriteToken(Roles.Semicolon, ";");
				}

				writer.EndNode(node);
			}
		}

		static bool HasParameters(ISymbol e)
		{
			switch (e.SymbolKind)
			{
				case SymbolKind.TypeDefinition:
					return ((ITypeDefinition)e).Kind == TypeKind.Delegate;
				case SymbolKind.Indexer:
				case SymbolKind.Method:
				case SymbolKind.Operator:
				case SymbolKind.Constructor:
				case SymbolKind.Destructor:
					return true;
				default:
					return false;
			}
		}

		TypeSystemAstBuilder CreateAstBuilder()
		{
			TypeSystemAstBuilder astBuilder = new() {
				AddResolveResultAnnotations = true,
				ShowTypeParametersForUnboundTypes = true,
				ShowModifiers = (ConversionFlags & ConversionFlags.ShowModifiers) == ConversionFlags.ShowModifiers,
				ShowAccessibility = (ConversionFlags & ConversionFlags.ShowAccessibility) ==
				                    ConversionFlags.ShowAccessibility,
				AlwaysUseShortTypeNames = (ConversionFlags & ConversionFlags.UseFullyQualifiedTypeNames) !=
				                          ConversionFlags.UseFullyQualifiedTypeNames,
				ShowParameterNames = (ConversionFlags & ConversionFlags.ShowParameterNames) ==
				                     ConversionFlags.ShowParameterNames,
				UseNullableSpecifierForValueTypes =
					(ConversionFlags & ConversionFlags.UseNullableSpecifierForValueTypes) != 0,
				SupportInitAccessors = (ConversionFlags & ConversionFlags.SupportInitAccessors) != 0,
				SupportRecordClasses = (ConversionFlags & ConversionFlags.SupportRecordClasses) != 0,
				SupportRecordStructs = (ConversionFlags & ConversionFlags.SupportRecordStructs) != 0
			};
			return astBuilder;
		}

		void WriteTypeDeclarationName(ITypeDefinition typeDef, TokenWriter writer,
			CSharpFormattingOptions formattingPolicy)
		{
			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			EntityDeclaration? node = astBuilder.ConvertEntity(typeDef);
			if (typeDef.DeclaringTypeDefinition != null &&
			    ((ConversionFlags & ConversionFlags.ShowDeclaringType) == ConversionFlags.ShowDeclaringType ||
			     (ConversionFlags & ConversionFlags.UseFullyQualifiedEntityNames) ==
			     ConversionFlags.UseFullyQualifiedEntityNames))
			{
				WriteTypeDeclarationName(typeDef.DeclaringTypeDefinition, writer, formattingPolicy);
				writer.WriteToken(Roles.Dot, ".");
			}
			else if ((ConversionFlags & ConversionFlags.UseFullyQualifiedEntityNames) ==
			         ConversionFlags.UseFullyQualifiedEntityNames)
			{
				if (!string.IsNullOrEmpty(typeDef.Namespace))
				{
					WriteQualifiedName(typeDef.Namespace, writer, formattingPolicy);
					writer.WriteToken(Roles.Dot, ".");
				}
			}

			writer.WriteIdentifier(node.NameToken);
			WriteTypeParameters(node, writer, formattingPolicy);
		}

		void WriteMemberDeclarationName(IMember member, TokenWriter writer, CSharpFormattingOptions formattingPolicy)
		{
			TypeSystemAstBuilder astBuilder = CreateAstBuilder();
			EntityDeclaration? node = astBuilder.ConvertEntity(member);
			if ((ConversionFlags & ConversionFlags.ShowDeclaringType) == ConversionFlags.ShowDeclaringType &&
			    member is not LocalFunctionMethod)
			{
				ConvertType(member.DeclaringType, writer, formattingPolicy);
				writer.WriteToken(Roles.Dot, ".");
			}

			switch (member.SymbolKind)
			{
				case SymbolKind.Indexer:
					writer.WriteKeyword(Roles.Identifier, "this");
					break;
				case SymbolKind.Constructor:
					WriteQualifiedName(member.DeclaringType.Name, writer, formattingPolicy);
					break;
				case SymbolKind.Destructor:
					writer.WriteToken(DestructorDeclaration.TildeRole, "~");
					WriteQualifiedName(member.DeclaringType.Name, writer, formattingPolicy);
					break;
				case SymbolKind.Operator:
					switch (member.Name)
					{
						case "op_Implicit":
							writer.WriteKeyword(OperatorDeclaration.ImplicitRole, "implicit");
							writer.Space();
							writer.WriteKeyword(OperatorDeclaration.OperatorKeywordRole, "operator");
							writer.Space();
							ConvertType(member.ReturnType, writer, formattingPolicy);
							break;
						case "op_Explicit":
							writer.WriteKeyword(OperatorDeclaration.ExplicitRole, "explicit");
							writer.Space();
							writer.WriteKeyword(OperatorDeclaration.OperatorKeywordRole, "operator");
							writer.Space();
							ConvertType(member.ReturnType, writer, formattingPolicy);
							break;
						default:
							writer.WriteKeyword(OperatorDeclaration.OperatorKeywordRole, "operator");
							writer.Space();
							var operatorType = OperatorDeclaration.GetOperatorType(member.Name);
							if (operatorType.HasValue)
								writer.WriteToken(OperatorDeclaration.GetRole(operatorType.Value),
									OperatorDeclaration.GetToken(operatorType.Value));
							else
								writer.WriteIdentifier(node.NameToken);
							break;
					}

					break;
				default:
					writer.WriteIdentifier(Identifier.Create(member.Name));
					break;
			}

			WriteTypeParameters(node, writer, formattingPolicy);
		}

		void WriteTypeParameters(EntityDeclaration? node, TokenWriter writer, CSharpFormattingOptions formattingPolicy)
		{
			if ((ConversionFlags & ConversionFlags.ShowTypeParameterList) == ConversionFlags.ShowTypeParameterList)
			{
				var outputVisitor = new CSharpOutputVisitor(writer, formattingPolicy);
				IEnumerable<TypeParameterDeclaration?> typeParameters = node.GetChildrenByRole(Roles.TypeParameter);
				if ((ConversionFlags & ConversionFlags.ShowTypeParameterVarianceModifier) == 0)
				{
					typeParameters = typeParameters.Select(RemoveVarianceModifier);
				}

				outputVisitor.WriteTypeParameters(typeParameters);
			}

			static TypeParameterDeclaration RemoveVarianceModifier(TypeParameterDeclaration? decl)
			{
				decl.Variance = VarianceModifier.Invariant;
				return decl;
			}
		}

		void PrintModifiers(Modifiers modifiers, TokenWriter writer)
		{
			foreach (var m in CSharpModifierToken.AllModifiers)
			{
				if ((modifiers & m) == m)
				{
					writer.WriteKeyword(EntityDeclaration.ModifierRole, CSharpModifierToken.GetModifierName(m));
					writer.Space();
				}
			}
		}

		void WriteQualifiedName(string name, TokenWriter writer, CSharpFormattingOptions formattingPolicy)
		{
			var node = AstType.Create(name);
			var outputVisitor = new CSharpOutputVisitor(writer, formattingPolicy);
			node.AcceptVisitor(outputVisitor);
		}

		#endregion
	}
}