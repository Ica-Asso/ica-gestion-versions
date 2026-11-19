using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace ICA.Desktop.Outils;

/// <summary>
/// Fenêtre sans barre de titre Windows : la barre est intégrée à l'application (même couleur),
/// coins arrondis et ombre natives sous Windows 11.
/// </summary>
public static class FenetreArrondie
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Appliquer(Window fenetre, bool redimensionnable)
    {
        WindowChrome.SetWindowChrome(fenetre, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = redimensionnable ? new Thickness(6) : new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        fenetre.SourceInitialized += (_, _) =>
        {
            try
            {
                var hwnd = new WindowInteropHelper(fenetre).Handle;
                var preference = DwmwcpRound;
                _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            }
            catch (DllNotFoundException) { /* Windows sans DWM : coins droits, sans gravité */ }
            catch (EntryPointNotFoundException) { }
        };

        // Une fenêtre sans cadre agrandie déborde de l'écran : on compense.
        if (redimensionnable)
        {
            fenetre.StateChanged += (_, _) => AjusterMarge(fenetre);
            fenetre.Loaded += (_, _) => AjusterMarge(fenetre);
        }
    }

    private static void AjusterMarge(Window fenetre)
    {
        if (fenetre.Content is not FrameworkElement racine) return;
        var bord = SystemParameters.WindowResizeBorderThickness;
        racine.Margin = fenetre.WindowState == WindowState.Maximized
            ? new Thickness(bord.Left + 1, bord.Top + 1, bord.Right + 1, bord.Bottom + 1)
            : new Thickness(0);
    }
}
