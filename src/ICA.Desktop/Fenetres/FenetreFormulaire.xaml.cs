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
        // Jamais plus haute que l'écran (barre des tâches comprise) : le contenu défile à l'intérieur,
        // et la croix comme les boutons Annuler / Enregistrer restent visibles.
        var ecran = SystemParameters.WorkArea;
        MaxHeight = ecran.Height - 20;
        vm.Fermer += (_, valide) => DialogResult = valide;
        Loaded += (_, _) =>
        {
            if (Top < ecran.Top || Top + ActualHeight > ecran.Bottom) Top = ecran.Top + Math.Max(10, (ecran.Height - ActualHeight) / 2);
            MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        };
    }
}
