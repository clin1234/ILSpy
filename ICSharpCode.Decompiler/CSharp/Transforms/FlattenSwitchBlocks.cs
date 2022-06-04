using System.Linq;

using ICSharpCode.Decompiler.CSharp.Syntax;

namespace ICSharpCode.Decompiler.CSharp.Transforms
{
	class FlattenSwitchBlocks : IAstTransform
	{
		public void Run(AstNode rootNode, TransformContext context)
		{
			foreach (var switchSection in rootNode.Descendants.OfType<SwitchSection>())
			{
				if (switchSection.Statements.Count != 1)
					continue;

				if (switchSection.Statements.First() is not BlockStatement blockStatement ||
				    blockStatement.Statements.Any(ContainsLocalDeclaration))
					continue;

				blockStatement.Remove();
				blockStatement.Statements.MoveTo(switchSection.Statements);
			}

			bool ContainsLocalDeclaration(AstNode node)
			{
				if (node is VariableDeclarationStatement or LocalFunctionDeclarationStatement
				    or OutVarDeclarationExpression)
					return true;
				if (node is BlockStatement)
					return false;
				foreach (var child in node.Children)
				{
					if (ContainsLocalDeclaration(child))
						return true;
				}

				return false;
			}
		}
	}
}