using System.Globalization;
using System.Text;

namespace ICA.Client.Journal;

/// <summary>
/// Journal technique. Règle (CDC §57) : jamais de nom, e-mail, téléphone, contenu de mail ni jeton.
/// N'y écrire que des identifiants techniques et des messages d'erreur.
/// </summary>
public interface IJournal
{
    void Info(string message);
    void Avertissement(string message);
    void Erreur(string message, Exception? exception = null);
    IReadOnlyList<string> DernieresLignes(int nombre);
    string Dossier { get; }
}

public sealed class JournalFichier : IJournal
{
    private const int JoursConserves = 14;
    private readonly Lock _verrou = new();

    public JournalFichier(string dossier)
    {
        Dossier = dossier;
        Directory.CreateDirectory(dossier);
        Purger();
    }

    public string Dossier { get; }

    private string FichierDuJour => Path.Combine(Dossier, $"ica-{DateTime.Now:yyyy-MM-dd}.log");

    public void Info(string message) => Ecrire("INFO", message);
    public void Avertissement(string message) => Ecrire("AVERT", message);

    public void Erreur(string message, Exception? exception = null)
    {
        var texte = exception is null
            ? message
            : $"{message} | {exception}";
        Ecrire("ERREUR", texte);
    }

    private void Ecrire(string niveau, string message)
    {
        var ligne = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} [{niveau}] {message}{Environment.NewLine}";
        lock (_verrou)
        {
            try { File.AppendAllText(FichierDuJour, ligne, Encoding.UTF8); }
            catch (IOException) { /* le journal ne doit jamais faire planter l'application */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<string> DernieresLignes(int nombre)
    {
        lock (_verrou)
        {
            try
            {
                var fichiers = Directory.GetFiles(Dossier, "ica-*.log").OrderDescending().Take(2).Reverse();
                var lignes = fichiers.SelectMany(f => File.ReadAllLines(f, Encoding.UTF8)).ToList();
                return lignes.Skip(Math.Max(0, lignes.Count - nombre)).ToList();
            }
            catch (IOException)
            {
                return [];
            }
        }
    }

    private void Purger()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Dossier, "ica-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-JoursConserves))
                    File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
