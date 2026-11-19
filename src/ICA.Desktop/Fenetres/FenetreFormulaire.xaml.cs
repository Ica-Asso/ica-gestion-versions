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
        // Jamais plus haute que l'écran où est ouverte l'application (plusieurs écrans possibles) :
        // le contenu défile à l'intérieur, la croix et les boutons Annuler / Enregistrer restent visibles.
        vm.Fermer += (_, valide) => DialogResult = valide;
        SourceInitialized += (_, _) => MaxHeight = Ecrans.ZoneUtile(Owner ?? this).Height - 20;
        Loaded += (_, _) =>
        {
            var ecran = Ecrans.ZoneUtile(Owner ?? this);
            MaxHeight = ecran.Height - 20;
            UpdateLayout();
            var hauteur = Math.Min(ActualHeight, MaxHeight);
            var largeur = ActualWidth;
            // Centrée dans l'écran de l'application, entièrement visible.
            var centreX = ecran.Left + ecran.Width / 2;
            var centreY = ecran.Top + ecran.Height / 2;
            Left = Math.Clamp(centreX - largeur / 2, ecran.Left, Math.Max(ecran.Left, ecran.Right - largeur));
            Top = Math.Clamp(centreY - hauteur / 2, ecran.Top + 10, Math.Max(ecran.Top + 10, ecran.Bottom - hauteur - 10));
            MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First));
        };
    }
}
