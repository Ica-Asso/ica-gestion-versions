using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ICA.Desktop.Outils;

public sealed class BoolVersVisibilite : IValueConverter
{
    public bool Inverser { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is true) ^ Inverser ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is Visibility.Visible) ^ Inverser;
}

/// <summary>Visible si la valeur n'est ni nulle ni une chaîne vide.</summary>
public sealed class NulVersVisibilite : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>« adherents » → la géométrie de ressource « Ico.adherents ».</summary>
public sealed class IdVersIcone : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string id ? Application.Current.TryFindResource("Ico." + id) as Geometry : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
