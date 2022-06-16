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

using ICSharpCode.Decompiler.TypeSystem;
using ICSharpCode.ILSpy.Properties;
using ICSharpCode.ILSpy.TreeNodes;

namespace ICSharpCode.ILSpy.Analyzers
{
	[ExportContextMenuEntry(Header = nameof(Resources.Analyze), Icon = "Images/Search", Category = nameof(Resources.Analyze), InputGestureText = "Ctrl+R", Order = 100)]
	internal sealed class AnalyzeCommand : SimpleCommand, IContextMenuEntry
	{
		public bool IsVisible(TextViewContext context)
		{
			if (context.TreeView is AnalyzerTreeView && context.SelectedTreeNodes != null && context.SelectedTreeNodes.All(n => n.Parent.IsRoot))
				return false;
			if (context.SelectedTreeNodes == null)
				return context.Reference != null && IsValidReference(context.Reference.Reference);
			return context.SelectedTreeNodes.All(n => n is IMemberTreeNode);
		}

		public bool IsEnabled(TextViewContext context)
		{
			if (context.SelectedTreeNodes == null)
				return context.Reference is { Reference: IEntity };
			foreach (var sharpTreeNode in context.SelectedTreeNodes)
			{
				var node = (IMemberTreeNode)sharpTreeNode;
				if (!IsValidReference(node.Member))
					return false;
			}

			return true;
		}

		bool IsValidReference(object reference)
		{
			return reference is IEntity && !(reference is IField { IsConst: true });
		}

		public void Execute(TextViewContext context)
		{
			AnalyzerTreeView analyzerTreeView = MainWindow.Instance.AnalyzerTreeView;
			if (analyzerTreeView == null)
			{
				return;
			}
			if (context.SelectedTreeNodes != null)
			{
				foreach (var node in context.SelectedTreeNodes.Cast<IMemberTreeNode>())
				{
					analyzerTreeView.Analyze(node.Member);
				}
			}
			else if (context.Reference is { Reference: IEntity entity })
			{
				analyzerTreeView.Analyze(entity);
			}
		}

		public override bool CanExecute(object parameter)
		{
			AnalyzerTreeView analyzerTreeView = MainWindow.Instance.AnalyzerTreeView;
			if (analyzerTreeView is { IsKeyboardFocusWithin: true })
			{
				return analyzerTreeView.SelectedItems.OfType<object>().All(n => n is IMemberTreeNode);
			}
			else
			{
				return MainWindow.Instance.SelectedNodes.All(n => n is IMemberTreeNode);
			}
		}

		public override void Execute(object parameter)
		{
			AnalyzerTreeView analyzerTreeView = MainWindow.Instance.AnalyzerTreeView;
			if (analyzerTreeView is { IsKeyboardFocusWithin: true })
			{
				foreach (IMemberTreeNode node in MainWindow.Instance.AnalyzerTreeView.SelectedItems.OfType<IMemberTreeNode>().ToArray())
				{
					MainWindow.Instance.AnalyzerTreeView.Analyze(node.Member);
				}
			}
			else
			{
				foreach (var node in MainWindow.Instance.SelectedNodes.Cast<IMemberTreeNode>())
				{
					MainWindow.Instance.AnalyzerTreeView.Analyze(node.Member);
				}
			}
		}
	}
}
