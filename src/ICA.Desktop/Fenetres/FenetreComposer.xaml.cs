using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetreComposer : Window
{
    public FenetreComposer(ComposerMessageViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: true);
        DataContext = vm;
        vm.Fermer += (_, _) => Close();
        Loaded += (_, _) => ChampCorps.Focus();
    }
}
