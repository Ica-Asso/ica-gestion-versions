using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetreEditionAdherent : Window
{
    public FenetreEditionAdherent(EditionAdherentViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        DataContext = vm;
        vm.Fermer += (_, enregistre) => DialogResult = enregistre;
        Loaded += (_, _) => PremierChamp.Focus();
    }
}
