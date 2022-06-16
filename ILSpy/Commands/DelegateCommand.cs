using System;
using System.Windows.Input;

namespace ICSharpCode.ILSpy.Commands
{
	internal sealed class DelegateCommand : ICommand
	{
		private readonly Action action;
		private readonly Func<bool> canExecute;

		public event EventHandler CanExecuteChanged {
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public DelegateCommand(Action action)
			: this(action, () => true)
		{
		}

		public DelegateCommand(Action action, Func<bool> canExecute)
		{
			this.action = action;
			this.canExecute = canExecute;
		}

		public bool CanExecute(object parameter)
		{
			return canExecute();
		}

		public void Execute(object parameter)
		{
			action();
		}
	}

	internal sealed class DelegateCommand<T> : ICommand
	{
		private readonly Action<T> action;
		private readonly Func<T, bool> canExecute;

		public event EventHandler CanExecuteChanged {
			add { CommandManager.RequerySuggested += value; }
			remove { CommandManager.RequerySuggested -= value; }
		}

		public DelegateCommand(Action<T> action)
			: this(action, _ => true)
		{
		}

		public DelegateCommand(Action<T> action, Func<T, bool> canExecute)
		{
			this.action = action;
			this.canExecute = canExecute;
		}

		public bool CanExecute(object parameter)
		{
			return canExecute((T)parameter);
		}

		public void Execute(object parameter)
		{
			action((T)parameter);
		}
	}
}
