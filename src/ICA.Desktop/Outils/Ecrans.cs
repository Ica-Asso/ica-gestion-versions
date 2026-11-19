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
