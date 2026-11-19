using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetreAssistance : Window
{
    public FenetreAssistance(AssistanceViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        DataContext = vm;
        vm.Fermer += (_, _) => Close();
        Loaded += (_, _) => ChampMessage.Focus();
    }
}
