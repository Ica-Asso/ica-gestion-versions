using System.Windows;
using ICA.Client.ViewModels;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

/// <summary>Fenêtre de saisie générique : le contenu dépend du type de formulaire (modèles dans le XAML).</summary>
public partial class FenetreFormulaire : Window
{
    public FenetreFormulaire(FormulaireViewModel vm)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        DataContext = vm;
        Width = vm.LargeurFenetre;
        vm.Fermer += (_, valide) => DialogResult = valide;
        Loaded += (_, _) =>
        {
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        };
    }
}
