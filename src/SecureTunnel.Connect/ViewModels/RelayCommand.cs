using System.Windows.Input;

namespace SecureTunnel.Connect.ViewModels;

/// <summary>Minimal ICommand adapter for an async, parameterless action.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;

    public RelayCommand(Func<Task> execute)
    {
        _execute = execute;
    }

    // ICommand requires this event even though this command's
    // CanExecute never changes.
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter) => await _execute();
}
