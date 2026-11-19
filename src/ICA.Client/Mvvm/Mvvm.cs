using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ICA.Client.Mvvm;

/// <summary>Base des ViewModels (fonctionne avec WPF comme avec MAUI).</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? nom = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));

    protected bool SetProperty<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
    {
        if (EqualityComparer<T>.Default.Equals(champ, valeur)) return false;
        champ = valeur;
        OnPropertyChanged(nom);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _executer;
    private readonly Func<object?, bool>? _peutExecuter;

    public RelayCommand(Action executer, Func<bool>? peutExecuter = null)
        : this(_ => executer(), peutExecuter is null ? null : _ => peutExecuter()) { }

    public RelayCommand(Action<object?> executer, Func<object?, bool>? peutExecuter = null)
    {
        _executer = executer;
        _peutExecuter = peutExecuter;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _peutExecuter?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _executer(parameter);
    }

    public void NotifierChangement() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>Commande asynchrone : désactivée pendant son exécution (évite les doubles clics).</summary>
public sealed class CommandeAsync(Func<Task> executer, Func<bool>? peutExecuter = null) : ICommand
{
    private bool _enCours;

    public event EventHandler? CanExecuteChanged;

    public bool EnCours => _enCours;

    public bool CanExecute(object? parameter) => !_enCours && (peutExecuter?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _enCours = true;
        NotifierChangement();
        try { await executer(); }
        finally
        {
            _enCours = false;
            NotifierChangement();
        }
    }

    public void NotifierChangement() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
