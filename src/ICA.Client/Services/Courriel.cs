using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ICA.Client.Services;

/// <summary>Où ouvrir les messages préparés par l'application.</summary>
public enum ModeMessagerie
{
    /// <summary>Page « Nouveau message » de Gmail, dans le navigateur habituel.</summary>
    Gmail = 0,

    /// <summary>Application de messagerie de l'ordinateur (Outlook, Courrier, Thunderbird…), via un lien mailto.</summary>
    Application = 1,
}

/// <summary>Réglages propres à cet ordinateur (non synchronisés) : %LOCALAPPDATA%\L-ICA\config\preferences.json.</summary>
public sealed class PreferencesLocales
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public ModeMessagerie Messagerie { get; set; } = ModeMessagerie.Gmail;

    [JsonIgnore]
    public string? Chemin { get; private set; }

    public static PreferencesLocales Charger(string chemin)
    {
        PreferencesLocales p;
        try
        {
            p = File.Exists(chemin)
                ? JsonSerializer.Deserialize<PreferencesLocales>(File.ReadAllText(chemin), Options) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            p = new();
        }
        p.Chemin = chemin;
        return p;
    }

    public void Enregistrer()
    {
        if (Chemin is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Chemin)!);
            File.WriteAllText(Chemin, JsonSerializer.Serialize(this, Options));
        }
        catch (IOException) { /* réglage de confort : on n'interrompt pas l'utilisatrice */ }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Liens qui ouvrent un message pré-rempli : Gmail dans le navigateur, ou messagerie de l'ordinateur.
/// v0.1 uniquement : à partir de la v0.8, l'envoi passera directement par Gmail (ARCHITECTURE.md §13).
/// </summary>
public static class LienCourriel
{
    /// <summary>Longueur de lien au-delà de laquelle le texte passe par le presse-papiers.</summary>
    public static int LongueurMaxLien(ModeMessagerie mode) => mode == ModeMessagerie.Gmail ? 7500 : 1900;

    public static int LongueurMaxCorps(ModeMessagerie mode) => mode == ModeMessagerie.Gmail ? 5000 : 1800;

    public static string Creer(ModeMessagerie mode, IEnumerable<string> a, string objet, string corps, IEnumerable<string>? cci = null)
        => mode == ModeMessagerie.Gmail ? Gmail(a, objet, corps, cci) : Mailto(a, objet, corps, cci);

    private static List<string> Nettoyer(IEnumerable<string>? adresses) =>
        adresses?.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

    private static string Mailto(IEnumerable<string> a, string objet, string corps, IEnumerable<string>? cci)
    {
        var destinataires = string.Join(',', Nettoyer(a).Select(Uri.EscapeDataString));
        var parametres = new List<string>();
        var copies = Nettoyer(cci);
        if (copies.Count > 0) parametres.Add("bcc=" + string.Join(',', copies.Select(Uri.EscapeDataString)));
        if (!string.IsNullOrEmpty(objet)) parametres.Add("subject=" + Uri.EscapeDataString(objet));
        if (!string.IsNullOrEmpty(corps))
            parametres.Add("body=" + Uri.EscapeDataString(Tronquer(corps, ModeMessagerie.Application).Replace("\r\n", "\n").Replace("\n", "\r\n")));

        var sb = new StringBuilder("mailto:").Append(destinataires);
        if (parametres.Count > 0) sb.Append('?').Append(string.Join('&', parametres));
        return sb.ToString();
    }

    private static string Gmail(IEnumerable<string> a, string objet, string corps, IEnumerable<string>? cci)
    {
        var parametres = new List<string> { "view=cm", "fs=1" };
        var destinataires = Nettoyer(a);
        var copies = Nettoyer(cci);
        if (destinataires.Count > 0) parametres.Add("to=" + Uri.EscapeDataString(string.Join(',', destinataires)));
        if (copies.Count > 0) parametres.Add("bcc=" + Uri.EscapeDataString(string.Join(',', copies)));
        if (!string.IsNullOrEmpty(objet)) parametres.Add("su=" + Uri.EscapeDataString(objet));
        if (!string.IsNullOrEmpty(corps)) parametres.Add("body=" + Uri.EscapeDataString(Tronquer(corps, ModeMessagerie.Gmail).Replace("\r\n", "\n")));
        return "https://mail.google.com/mail/?" + string.Join('&', parametres);
    }

    private static string Tronquer(string corps, ModeMessagerie mode)
    {
        var max = LongueurMaxCorps(mode);
        return corps.Length > max ? corps[..max] + "\n[…]" : corps;
    }
}
