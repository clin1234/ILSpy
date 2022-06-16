using ICSharpCode.ILSpy.Docking;
using ICSharpCode.ILSpy.ViewModels;

namespace ICSharpCode.ILSpy.Commands
{
	sealed class ToolPaneCommand : SimpleCommand
	{
		readonly string contentId;

		public ToolPaneCommand(string contentId)
		{
			this.contentId = contentId;
		}

		public override void Execute(object parameter)
		{
			DockWorkspace.Instance.ShowToolPane(contentId);
		}
	}

	sealed class TabPageCommand : SimpleCommand
	{
		readonly TabPageModel model;

		public TabPageCommand(TabPageModel model)
		{
			this.model = model;
		}

		public override void Execute(object parameter)
		{
			DockWorkspace.Instance.ActiveTabPage = model;
		}
	}
}