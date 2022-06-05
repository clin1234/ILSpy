﻿// Copyright (c) 2014 Daniel Grunwald
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

using ICSharpCode.Decompiler.CSharp.Syntax;
using ICSharpCode.Decompiler.Semantics;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	/// <summary>
	/// Escape invalid identifiers.
	/// </summary>
	/// <remarks>
	/// This transform is not enabled by default.
	/// </remarks>
	internal sealed class EscapeInvalidIdentifiers : IAstTransform
	{
		public void Run(AstNode rootNode, TransformContext context)
		{
			foreach (var ident in rootNode.DescendantsAndSelf.OfType<Identifier>())
			{
				ident.Name = ReplaceInvalid(ident.Name);
			}
		}

		bool IsValid(char ch)
		{
			if (char.IsLetterOrDigit(ch))
				return true;
			if (ch == '_')
				return true;
			return false;
		}

		string ReplaceInvalid(string s)
		{
			string name = string.Concat(s.Select(ch => IsValid(ch) ? ch.ToString() : $"_{(int)ch:X4}"));
			if (name.Length >= 1 && !(char.IsLetter(name[0]) || name[0] == '_'))
				name = "_" + name;
			return name;
		}
	}

	/// <summary>
	/// This transform is used to remove assembly-attributes that are generated by the compiler,
	/// thus don't need to be declared. (We have to remove them, in order to avoid conflicts while compiling.)
	/// </summary>
	/// <remarks>This transform is only enabled, when exporting a full assembly as project.</remarks>
	internal sealed class RemoveCompilerGeneratedAssemblyAttributes : IAstTransform
	{
		public void Run(AstNode rootNode, TransformContext context)
		{
			foreach (var section in rootNode.Children.OfType<AttributeSection>())
			{
				switch (section.AttributeTarget)
				{
					case "assembly":
					{
						foreach (var attribute in section.Attributes)
						{
							var trr = attribute.Type.Annotation<TypeResolveResult>();
							if (trr == null)
								continue;

							string fullName = trr.Type.FullName;
							var arguments = attribute.Arguments;
							switch (fullName)
							{
								case "System.Diagnostics.DebuggableAttribute":
								{
									attribute.Remove();
									break;
								}
								case "System.Runtime.CompilerServices.CompilationRelaxationsAttribute":
								{
									if (arguments.Count == 1 && arguments.First() is PrimitiveExpression {
										    Value: 8
									    })
										attribute.Remove();
									break;
								}
								case "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute":
								{
									if (arguments.Count != 1)
										break;
									if (arguments.First() is not NamedExpression {
										    Name: "WrapNonExceptionThrows",
										    Expression: PrimitiveExpression { Value: true }
									    })
										break;
									attribute.Remove();
									break;
								}
								case "System.Runtime.Versioning.TargetFrameworkAttribute":
								{
									attribute.Remove();
									break;
								}
								case "System.Security.Permissions.SecurityPermissionAttribute":
								{
									if (arguments.Count != 2)
										break;
									if (arguments.First() is not MemberReferenceExpression {
										    MemberName: "RequestMinimum",
										    NextSibling: NamedExpression {
											    Name: "SkipVerification",
											    Expression: PrimitiveExpression { Value: true }
										    }
									    })
										break;
									attribute.Remove();
									break;
								}
							}
						}

						break;
					}
					case "module":
					{
						foreach (var attribute in section.Attributes)
						{
							var trr = attribute.Type.Annotation<TypeResolveResult>();
							if (trr == null)
								continue;

							switch (trr.Type.FullName)
							{
								case "System.Security.UnverifiableCodeAttribute":
									attribute.Remove();
									break;
							}
						}

						break;
					}
					default:
						continue;
				}

				if (section.Attributes.Count == 0)
				{
					section.Remove();
				}
			}
		}
	}
}