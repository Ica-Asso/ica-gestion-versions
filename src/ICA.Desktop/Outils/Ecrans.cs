using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ICA.Desktop.Outils;

/// <summary>Zone utile (sans la barre des tâches) de l'écran où se trouve une fenêtre, en unités WPF.</summary>
public static class Ecrans
{
    public static Rect ZoneUtile(Window fenetre)
    {
        try
        {
            var handle = new WindowInteropHelper(fenetre).Handle;
            if (handle == IntPtr.Zero) return SystemParameters.WorkArea;
            var moniteur = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Taille = Marshal.SizeOf<MonitorInfo>() };
            if (moniteur == IntPtr.Zero || !GetMonitorInfo(moniteur, ref info)) return SystemParameters.WorkArea;

            // Pixels physiques → unités WPF (écrans avec zoom 125 %, 150 %…).
            var source = PresentationSource.FromVisual(fenetre);
            var vers = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
            var hautGauche = vers.Transform(new Point(info.Travail.Gauche, info.Travail.Haut));
            var basDroite = vers.Transform(new Point(info.Travail.Droite, info.Travail.Bas));
            return new Rect(hautGauche, basDroite);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return SystemParameters.WorkArea;
        }
    }

    /// <summary>
    /// Garde une fenêtre entièrement dans l'écran où elle s'affiche : taille réduite si elle est plus grande
    /// que l'écran (le contenu défile), position recalée si elle déborde (plusieurs écrans, petit écran, zoom Windows).
    /// </summary>
    public static void GarderDans(Window f)
    {
        if (f.WindowState != WindowState.Normal) return;
        var ecran = ZoneUtile(f.Owner ?? f);
        if (ecran.Width <= 0 || ecran.Height <= 0) return;

        f.MinWidth = Math.Min(f.MinWidth, ecran.Width);
        f.MinHeight = Math.Min(f.MinHeight, ecran.Height);
        f.MaxWidth = Math.Min(f.MaxWidth, ecran.Width);
        f.MaxHeight = Math.Min(f.MaxHeight, ecran.Height);
        if (f.ActualWidth > ecran.Width) f.Width = ecran.Width;
        if (f.ActualHeight > ecran.Height) f.Height = ecran.Height;
        f.UpdateLayout();

        var largeur = Math.Min(f.ActualWidth, ecran.Width);
        var hauteur = Math.Min(f.ActualHeight, ecran.Height);
        if (f.Left < ecran.Left || f.Left + largeur > ecran.Right)
            f.Left = Math.Clamp(f.Left, ecran.Left, ecran.Right - largeur);
        if (f.Top < ecran.Top || f.Top + hauteur > ecran.Bottom)
            f.Top = Math.Clamp(f.Top, ecran.Top, ecran.Bottom - hauteur);
    }

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle { public int Gauche, Haut, Droite, Bas; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Taille;
        public Rectangle Moniteur;
        public Rectangle Travail;
        public int Options;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int options);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr moniteur, ref MonitorInfo info);
}
