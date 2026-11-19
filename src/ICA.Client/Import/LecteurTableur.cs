using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace ICA.Client.Import;

/// <summary>
/// Lecture d'un tableau .xlsx ou .csv, sans bibliothèque externe.
/// Chaque ligne est un tableau de cellules : string (texte), double (nombre ou date Excel) ou null.
/// </summary>
public static class LecteurTableur
{
    public static IReadOnlyList<object?[]> Lire(string chemin) =>
        Path.GetExtension(chemin).ToLowerInvariant() switch
        {
            ".csv" or ".txt" => LireCsv(chemin),
            ".xlsx" or ".xlsm" => LireXlsx(chemin),
            ".xls" => throw new InvalidDataException(
                "Ce fichier est au format Excel ancien (.xls). Ouvrez-le dans Excel puis « Enregistrer sous » au format .xlsx."),
            _ => throw new InvalidDataException("Format non reconnu : choisissez un fichier Excel (.xlsx) ou CSV."),
        };

    // ---------------- XLSX ----------------

    public static IReadOnlyList<object?[]> LireXlsx(string chemin)
    {
        using var flux = new FileStream(chemin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(flux, ZipArchiveMode.Read);

        var partages = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var s = ss.Open();
            partages.AddRange(XDocument.Load(s).Descendants().Where(e => e.Name.LocalName == "si")
                .Select(si => string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))));
        }

        // On garde la feuille qui contient le plus de lignes remplies.
        IReadOnlyList<object?[]> meilleure = [];
        foreach (var entree in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                                                      && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using var s = entree.Open();
            var lignes = LireFeuille(XDocument.Load(s), partages);
            if (lignes.Count(l => l.Any(c => c is not null)) > meilleure.Count(l => l.Any(c => c is not null)))
                meilleure = lignes;
        }
        return meilleure;
    }

    private static List<object?[]> LireFeuille(XDocument doc, List<string> partages)
    {
        var parLigne = new SortedDictionary<int, Dictionary<int, object?>>();
        var maxCol = 0;
        foreach (var row in doc.Descendants().Where(e => e.Name.LocalName == "row"))
        {
            var numero = int.TryParse((string?)row.Attribute("r"), out var r) ? r : parLigne.Count + 1;
            var cellules = new Dictionary<int, object?>();
            var position = 0;
            foreach (var c in row.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var col = (string?)c.Attribute("r") is { } reference ? IndexColonne(reference) : position;
                position = col + 1;
                var valeur = ValeurCellule(c, partages);
                if (valeur is string { Length: 0 }) valeur = null;
                if (valeur is null) continue;
                cellules[col] = valeur;
                maxCol = Math.Max(maxCol, col + 1);
            }
            if (cellules.Count > 0) parLigne[numero] = cellules;
        }

        var resultat = new List<object?[]>();
        if (parLigne.Count == 0) return resultat;
        for (var n = 1; n <= parLigne.Keys.Max(); n++)
        {
            var ligne = new object?[maxCol];
            if (parLigne.TryGetValue(n, out var cellules))
                foreach (var (col, v) in cellules) ligne[col] = v;
            resultat.Add(ligne);
        }
        return resultat;
    }

    private static object? ValeurCellule(XElement c, List<string> partages)
    {
        var type = (string?)c.Attribute("t");
        var v = c.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
        switch (type)
        {
            case "s":
                return int.TryParse(v, out var i) && i >= 0 && i < partages.Count ? partages[i].Trim() : null;
            case "inlineStr":
                return string.Concat(c.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)).Trim();
            case "str":
            case "e":
                return v?.Trim();
            case "b":
                return v == "1" ? "VRAI" : "FAUX";
            default:
                return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : v?.Trim();
        }
    }

    /// <summary>« C12 » → 2.</summary>
    public static int IndexColonne(string reference)
    {
        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }

    public static string LettreColonne(int index)
    {
        var s = "";
        for (index++; index > 0; index = (index - 1) / 26)
            s = (char)('A' + (index - 1) % 26) + s;
        return s;
    }

    // ---------------- CSV ----------------

    public static IReadOnlyList<object?[]> LireCsv(string chemin)
    {
        var octets = File.ReadAllBytes(chemin);
        string texte;
        try
        {
            texte = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(octets);
        }
        catch (DecoderFallbackException)
        {
            texte = Encoding.Latin1.GetString(octets); // ancien CSV Excel « Windows »
        }
        texte = texte.TrimStart('﻿');

        var premiere = texte.Split('\n').FirstOrDefault(l => l.Trim().Length > 0) ?? "";
        var separateur = premiere.Count(ch => ch == ';') >= premiere.Count(ch => ch == ',') ? ';' : ',';
        if (premiere.Count(ch => ch == '\t') > premiere.Count(ch => ch == separateur)) separateur = '\t';

        var lignes = new List<object?[]>();
        var cellules = new List<object?>();
        var courant = new StringBuilder();
        var entreGuillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var ch = texte[i];
            if (entreGuillemets)
            {
                if (ch == '"' && i + 1 < texte.Length && texte[i + 1] == '"') { courant.Append('"'); i++; }
                else if (ch == '"') entreGuillemets = false;
                else courant.Append(ch);
            }
            else if (ch == '"') entreGuillemets = true;
            else if (ch == separateur) { cellules.Add(Cellule(courant)); courant.Clear(); }
            else if (ch == '\n')
            {
                cellules.Add(Cellule(courant)); courant.Clear();
                lignes.Add(cellules.ToArray()); cellules.Clear();
            }
            else if (ch != '\r') courant.Append(ch);
        }
        if (courant.Length > 0 || cellules.Count > 0) { cellules.Add(Cellule(courant)); lignes.Add(cellules.ToArray()); }
        return lignes;
    }

    private static object? Cellule(StringBuilder sb)
    {
        var s = sb.ToString().Trim();
        return s.Length == 0 ? null : s;
    }
}
