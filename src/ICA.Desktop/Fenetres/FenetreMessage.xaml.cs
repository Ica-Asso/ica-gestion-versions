using System.Windows;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

/// <summary>Remplace MessageBox : mêmes couleurs que l'application, gros boutons, textes clairs.</summary>
public partial class FenetreMessage : Window
{
    private FenetreMessage(string titre, string message, string boutonOui, string? boutonNon, bool danger)
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: false);
        TexteTitre.Text = titre;
        TexteMessage.Text = message;
        BoutonOui.Content = boutonOui;
        if (danger) BoutonOui.Style = (Style)FindResource("BoutonDangerPlein");
        if (boutonNon is null) BoutonNon.Visibility = Visibility.Collapsed;
        else BoutonNon.Content = boutonNon;
    }

    public static bool Confirmer(Window? proprietaire, string titre, string message, string boutonOui, bool danger)
    {
        var f = new FenetreMessage(titre, message, boutonOui, "Annuler", danger) { Owner = proprietaire };
        if (proprietaire is null) f.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return f.ShowDialog() == true;
    }

    public static void Informer(Window? proprietaire, string titre, string message)
    {
        var f = new FenetreMessage(titre, message, "OK", null, false) { Owner = proprietaire };
        if (proprietaire is null) f.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        f.ShowDialog();
    }

    private void Oui(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Non(object sender, RoutedEventArgs e) => DialogResult = false;
}
