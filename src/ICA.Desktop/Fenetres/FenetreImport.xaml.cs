using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetreImport : Window
{
    public FenetreImport(ImportViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: true);
        DataContext = vm;
        vm.Fermer += (_, importe) => DialogResult = importe;
    }
}
