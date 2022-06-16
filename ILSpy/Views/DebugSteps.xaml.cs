using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;
using ICSharpCode.ILSpy.Docking;
using ICSharpCode.ILSpy.ViewModels;

namespace ICSharpCode.ILSpy
{
	public sealed partial class DebugSteps
	{
		static readonly ILAstWritingOptions writingOptions = new() {
			UseFieldSugar = true,
			UseLogicOperationSugar = true
		};
		
		public static ILAstWritingOptions Options => writingOptions;

#if DEBUG
		ILAstLanguage? language;
#endif
		FilterSettings filterSettings;

		public DebugSteps()
		{
			InitializeComponent();

#if DEBUG
			DockWorkspace.Instance.PropertyChanged += DockWorkspace_PropertyChanged;
			filterSettings = DockWorkspace.Instance.ActiveTabPage.FilterSettings;
			filterSettings.PropertyChanged += FilterSettings_PropertyChanged;
			MainWindow.Instance.SelectionChanged += SelectionChanged;
			writingOptions.PropertyChanged += WritingOptions_PropertyChanged;

			if (MainWindow.Instance.CurrentLanguage is ILAstLanguage l)
			{
				l.StepperUpdated += ILAstStepperUpdated;
				language = l;
				ILAstStepperUpdated(null, null);
			}
#endif
		}

		private void DockWorkspace_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(DockWorkspace.Instance.ActiveTabPage):
					filterSettings.PropertyChanged -= FilterSettings_PropertyChanged;
					filterSettings = DockWorkspace.Instance.ActiveTabPage.FilterSettings;
					filterSettings.PropertyChanged += FilterSettings_PropertyChanged;
					break;
			}
		}

		private void WritingOptions_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			DecompileAsync(lastSelectedStep);
		}

		private void SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			Dispatcher.Invoke(() => {
				tree.ItemsSource = null;
				lastSelectedStep = int.MaxValue;
			});
		}

		private void FilterSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
#if DEBUG
			if (e.PropertyName == "Language")
			{
				if (language != null)
				{
					language.StepperUpdated -= ILAstStepperUpdated;
				}
				if (MainWindow.Instance.CurrentLanguage is ILAstLanguage l)
				{
					l.StepperUpdated += ILAstStepperUpdated;
					language = l;
					ILAstStepperUpdated(null, null);
				}
			}
#endif
		}

		private void ILAstStepperUpdated(object? sender, EventArgs? e)
		{
#if DEBUG
			if (language == null)
				return;
			Dispatcher.Invoke(() => {
				tree.ItemsSource = language.Stepper.Steps;
				lastSelectedStep = int.MaxValue;
			});
#endif
		}

		private void ShowStateAfter_Click(object sender, RoutedEventArgs e)
		{
			if (tree.SelectedItem is not Stepper.Node n)
				return;
			DecompileAsync(n.EndStep);
		}

		private void ShowStateBefore_Click(object sender, RoutedEventArgs e)
		{
			if (tree.SelectedItem is not Stepper.Node n)
				return;
			DecompileAsync(n.BeginStep);
		}

		private void DebugStep_Click(object sender, RoutedEventArgs e)
		{
			if (tree.SelectedItem is not Stepper.Node n)
				return;
			DecompileAsync(n.BeginStep, true);
		}

		int lastSelectedStep = int.MaxValue;

		void DecompileAsync(int step, bool isDebug = false)
		{
			lastSelectedStep = step;
			var window = MainWindow.Instance;
			var state = DockWorkspace.Instance.ActiveTabPage.GetState();
			DockWorkspace.Instance.ActiveTabPage.ShowTextViewAsync(textView => textView.DecompileAsync(window.CurrentLanguage, window.SelectedNodes,
				new DecompilationOptions(window.CurrentLanguageVersion) {
					StepLimit = step,
					IsDebug = isDebug,
					TextViewState = state as TextView.DecompilerTextViewState
				}));
		}

		private void tree_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key is Key.Enter or Key.Return)
			{
				if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
					ShowStateBefore_Click(sender, e);
				else
					ShowStateAfter_Click(sender, e);
				e.Handled = true;
			}
		}
	}
}