using System.Windows;
using ICA.Desktop.Outils;

namespace ICA.Desktop.Fenetres;

public partial class FenetrePrincipale : Window
{
    public FenetrePrincipale()
    {
        InitializeComponent();
        FenetreArrondie.Appliquer(this, redimensionnable: true);
    }

    private void Reduire(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void AgrandirRestaurer(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void Fermer(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
