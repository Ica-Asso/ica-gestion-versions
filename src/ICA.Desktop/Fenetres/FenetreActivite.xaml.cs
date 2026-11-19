using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetreActivite : Window
{
    public FenetreActivite(EditionActiviteViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        DataContext = vm;
        vm.Fermer += (_, enregistre) => DialogResult = enregistre;
        Loaded += (_, _) => ChampNom.Focus();
    }
}
