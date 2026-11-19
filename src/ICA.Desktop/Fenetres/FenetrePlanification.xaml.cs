using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetrePlanification : Window
{
    public FenetrePlanification(PlanificationViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        DataContext = vm;
        vm.Fermer += (_, enregistre) => DialogResult = enregistre;
    }
}
