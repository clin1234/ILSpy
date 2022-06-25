using System;

using Microsoft.VisualStudio.Shell;

namespace ICSharpCode.ILSpy.AddIn.Commands
{
	sealed class OpenProjectOutputCommand : ILSpyCommand
	{
		public OpenProjectOutputCommand(ILSpyAddInPackage owner)
			: base(owner, PkgCmdIDList.cmdidOpenProjectOutputInILSpy)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
		}

		protected override void OnBeforeQueryStatus(object? sender, EventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			if (sender is OleMenuCommand menuItem)
			{
				menuItem.Visible = false;

				var selectedItem = ILSpyAddInPackage.DTE.SelectedItems.Item(1);
				menuItem.Visible = (ProjectItemForILSpy.Detect(owner, selectedItem) != null);
			}
		}

		protected override void OnExecute(object? sender, EventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			if (ILSpyAddInPackage.DTE.SelectedItems.Count != 1)
				return;
			var projectItemWrapper = ProjectItemForILSpy.Detect(owner, ILSpyAddInPackage.DTE.SelectedItems.Item(1));
			if (projectItemWrapper != null)
			{
				OpenAssembliesInILSpy(projectItemWrapper.GetILSpyParameters(owner));
			}
		}

		internal static void Register(ILSpyAddInPackage owner)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			new OpenProjectOutputCommand(owner);
		}
	}
}
