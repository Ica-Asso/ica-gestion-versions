using System.Globalization;
using System.Text;

namespace ICA.Client.Documents;

/// <summary>
/// Petit générateur PDF autonome (aucune dépendance) : pages A4, polices Helvetica standard,
/// texte avec retour à la ligne, traits, cadres, cases à cocher et image JPEG (logo).
/// Les coordonnées sont en points (1/72 de pouce) depuis le coin HAUT gauche.
/// </summary>
public sealed class DocumentPdf
{
    public const float LargeurPage = 595.28f;
    public const float HauteurPage = 841.89f;

    private readonly List<StringBuilder> _pages = [];
    private readonly List<(byte[] Jpeg, int Largeur, int Hauteur, int Composantes)> _images = [];
    private StringBuilder? _page;

    public string Titre { get; set; } = "";

    /// <summary>Texte de pied de page, ajouté sur chaque page avec « Page n/N ».</summary>
    public string? PiedDePage { get; set; }

    /// <summary>Filigrane diagonal sur chaque page (ex. « SPÉCIMEN — NON VALABLE »).</summary>
    public string? Filigrane { get; set; }
    public int NombrePages => _pages.Count;

    public void NouvellePage()
    {
        _page = new StringBuilder();
        _pages.Add(_page);
    }

    private StringBuilder Page => _page ?? throw new InvalidOperationException("Appelez NouvellePage() d'abord.");

    private static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static float Y(float yHaut) => HauteurPage - yHaut;

    // ---------------- Texte ----------------

    public static float Largeur(string texte, float taille, bool gras = false)
    {
        var table = gras ? LargeursGras : LargeursNormal;
        var total = 0;
        foreach (var o in EncoderWinAnsi(texte))
            total += o >= 32 ? table[o - 32] : 0;
        return total * taille / 1000f;
    }

    /// <summary>Écrit une ligne ; y = ligne de base mesurée depuis le haut.</summary>
    public void Texte(float x, float y, string texte, float taille, bool gras = false, Couleur? couleur = null, Alignement alignement = Alignement.Gauche)
    {
        if (string.IsNullOrEmpty(texte)) return;
        if (alignement != Alignement.Gauche)
        {
            var l = Largeur(texte, taille, gras);
            x -= alignement == Alignement.Droite ? l : l / 2;
        }
        var c = couleur ?? Couleur.Noir;
        Page.Append($"BT {c.Remplissage()} /{(gras ? "F2" : "F1")} {N(taille)} Tf {N(x)} {N(Y(y))} Td (");
        foreach (var o in EncoderWinAnsi(texte))
        {
            if (o is (byte)'(' or (byte)')' or (byte)'\\') Page.Append('\\').Append((char)o);
            else if (o < 32 || o > 126) Page.Append('\\').Append(Convert.ToString(o, 8).PadLeft(3, '0'));
            else Page.Append((char)o);
        }
        Page.Append(") Tj ET\n");
    }

    /// <summary>Découpe le texte pour tenir dans la largeur (respecte les retours à la ligne).</summary>
    public static List<string> Couper(string texte, float largeur, float taille, bool gras = false)
    {
        var lignes = new List<string>();
        foreach (var paragraphe in texte.Replace("\r\n", "\n").Split('\n'))
        {
            var courante = "";
            foreach (var mot in paragraphe.Split(' '))
            {
                var essai = courante.Length == 0 ? mot : courante + " " + mot;
                if (Largeur(essai, taille, gras) <= largeur || courante.Length == 0)
                {
                    // Mot plus long que la ligne : on le coupe.
                    while (Largeur(essai, taille, gras) > largeur && essai.Length > 1)
                    {
                        var n = essai.Length - 1;
                        while (n > 1 && Largeur(essai[..n], taille, gras) > largeur) n--;
                        lignes.Add(essai[..n]);
                        essai = essai[n..];
                    }
                    courante = essai;
                }
                else
                {
                    lignes.Add(courante);
                    courante = mot;
                }
            }
            lignes.Add(courante);
        }
        return lignes;
    }

    /// <summary>Paragraphe multi-lignes ; renvoie le y sous la dernière ligne.</summary>
    public float Paragraphe(float x, float y, float largeur, string texte, float taille, bool gras = false, Couleur? couleur = null, float interligne = 1.3f)
    {
        foreach (var ligne in Couper(texte, largeur, taille, gras))
        {
            Texte(x, y, ligne, taille, gras, couleur);
            y += taille * interligne;
        }
        return y;
    }

    // ---------------- Dessin ----------------

    public void Trait(float x1, float y1, float x2, float y2, float epaisseur = 0.6f, Couleur? couleur = null)
        => Page.Append($"q {(couleur ?? Couleur.Noir).Trait()} {N(epaisseur)} w {N(x1)} {N(Y(y1))} m {N(x2)} {N(Y(y2))} l S Q\n");

    public void Cadre(float x, float y, float largeur, float hauteur, Couleur? remplissage = null, Couleur? trait = null, float epaisseur = 0.6f)
    {
        var ops = remplissage is not null && trait is not null ? "B" : remplissage is not null ? "f" : "S";
        Page.Append("q ");
        if (remplissage is { } r) Page.Append(r.Remplissage()).Append(' ');
        if (trait is { } t) Page.Append(t.Trait()).Append(' ').Append(N(epaisseur)).Append(" w ");
        else if (remplissage is null) Page.Append("0 G ").Append(N(epaisseur)).Append(" w ");
        Page.Append($"{N(x)} {N(Y(y + hauteur))} {N(largeur)} {N(hauteur)} re {ops} Q\n");
    }

    /// <summary>Case à cocher de 9 pt (y = haut de la case).</summary>
    public void Case(float x, float y, bool cochee)
    {
        Cadre(x, y, 9, 9, trait: Couleur.Noir, epaisseur: 0.7f);
        if (cochee)
        {
            Trait(x + 1.8f, y + 4.8f, x + 3.8f, y + 7.2f, 1.3f);
            Trait(x + 3.8f, y + 7.2f, x + 7.6f, y + 1.8f, 1.3f);
        }
    }

    /// <summary>Image JPEG (y = haut de l'image).</summary>
    public void Image(byte[] jpeg, float x, float y, float largeur, float hauteur)
    {
        var (l, h, comp) = LireJpeg(jpeg);
        _images.Add((jpeg, l, h, comp));
        Page.Append($"q {N(largeur)} 0 0 {N(hauteur)} {N(x)} {N(Y(y + hauteur))} cm /Im{_images.Count} Do Q\n");
    }

    // ---------------- Fichier ----------------

    public byte[] Octets()
    {
        if (_pages.Count == 0) NouvellePage();
        if (Filigrane is { } filigrane)
        {
            // Texte en diagonale (45°), rose pâle, centré, SOUS le contenu (le texte du reçu reste lisible).
            const float taille = 54;
            var largeur = Largeur(filigrane, taille, true);
            var cos = MathF.Cos(MathF.PI / 4);
            var x0 = LargeurPage / 2 - largeur / 2 * cos + taille / 3 * cos;
            var y0 = HauteurPage / 2 - largeur / 2 * cos - taille / 3 * cos;
            foreach (var p in _pages)
            {
                var f = new StringBuilder();
                f.Append($"q 0.98 0.78 0.8 rg BT /F2 {N(taille)} Tf {N(cos)} {N(cos)} {N(-cos)} {N(cos)} {N(x0)} {N(y0)} Tm (");
                foreach (var o in EncoderWinAnsi(filigrane))
                {
                    if (o is (byte)'(' or (byte)')' or (byte)'\\') f.Append('\\').Append((char)o);
                    else if (o < 32 || o > 126) f.Append('\\').Append(Convert.ToString(o, 8).PadLeft(3, '0'));
                    else f.Append((char)o);
                }
                f.Append(") Tj ET Q\n");
                p.Insert(0, f.ToString());
            }
            Filigrane = null;
        }
        if (PiedDePage is { } pied)
        {
            var courante = _page;
            for (var i = 0; i < _pages.Count; i++)
            {
                _page = _pages[i];
                Trait(50, HauteurPage - 44, LargeurPage - 50, HauteurPage - 44, 0.4f, Couleur.GrisClair);
                Texte(50, HauteurPage - 30, pied, 8, couleur: Couleur.Gris);
                Texte(LargeurPage - 50, HauteurPage - 30, $"Page {i + 1}/{_pages.Count}", 8, couleur: Couleur.Gris, alignement: Alignement.Droite);
            }
            _page = courante;
            PiedDePage = null;
        }
        using var flux = new MemoryStream();
        var positions = new List<long>();
        void Ecrire(string s) { var b = Encoding.Latin1.GetBytes(s); flux.Write(b); }
        void Objet(string contenu) { positions.Add(flux.Position); Ecrire($"{positions.Count} 0 obj\n{contenu}\nendobj\n"); }

        Ecrire("%PDF-1.4\n%âãÏÓ\n");
        // 1 catalogue, 2 pages, 3-4 polices, puis images, puis (contenu, page) par page, puis infos.
        var premiereImage = 5;
        var premierePage = premiereImage + _images.Count;
        var kids = string.Join(" ", Enumerable.Range(0, _pages.Count).Select(i => $"{premierePage + i * 2 + 1} 0 R"));

        Objet("<< /Type /Catalog /Pages 2 0 R >>");
        Objet($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
        Objet("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Objet("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        foreach (var (jpeg, l, h, comp) in _images)
        {
            positions.Add(flux.Position);
            var espace = comp == 1 ? "/DeviceGray" : comp == 4 ? "/DeviceCMYK" : "/DeviceRGB";
            Ecrire($"{positions.Count} 0 obj\n<< /Type /XObject /Subtype /Image /Width {l} /Height {h} /ColorSpace {espace} /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
            flux.Write(jpeg);
            Ecrire("\nendstream\nendobj\n");
        }

        var ressourcesImages = string.Join(" ", Enumerable.Range(1, _images.Count).Select(i => $"/Im{i} {premiereImage + i - 1} 0 R"));
        foreach (var page in _pages)
        {
            var contenu = Encoding.Latin1.GetBytes(page.ToString());
            positions.Add(flux.Position);
            Ecrire($"{positions.Count} 0 obj\n<< /Length {contenu.Length} >>\nstream\n");
            flux.Write(contenu);
            Ecrire("\nendstream\nendobj\n");
            Objet($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(LargeurPage)} {N(HauteurPage)}] /Contents {positions.Count} 0 R " +
                  $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << {ressourcesImages} >> >> >>");
        }

        Objet($"<< /Title {ChaineUtf16(Titre)} /Producer (L'ICA Gestion) >>");
        var infos = positions.Count;

        var xref = flux.Position;
        Ecrire($"xref\n0 {positions.Count + 1}\n0000000000 65535 f \n");
        foreach (var p in positions) Ecrire($"{p:0000000000} 00000 n \n");
        Ecrire($"trailer\n<< /Size {positions.Count + 1} /Root 1 0 R /Info {infos} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return flux.ToArray();
    }

    private static string ChaineUtf16(string s)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var c in s) sb.Append(((int)c).ToString("X4"));
        return sb.Append('>').ToString();
    }

    // ---------------- Outils ----------------

    /// <summary>Encodage WinAnsi (Windows-1252) : accents français, €, guillemets, tirets.</summary>
    public static byte[] EncoderWinAnsi(string texte)
    {
        var octets = new List<byte>(texte.Length);
        foreach (var c in texte)
        {
            octets.Add(c switch
            {
                ' ' or ' ' or ' ' => (byte)' ',
                '€' => 0x80, '‚' => 0x82, '„' => 0x84, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
                '•' => 0x95, '–' => 0x96, '—' => 0x97, 'œ' => 0x9C, 'Œ' => 0x8C, 'Ÿ' => 0x9F, '™' => 0x99,
                '\t' => (byte)' ',
                _ when c >= 32 && c <= 126 => (byte)c,
                _ when c >= 0xA1 && c <= 0xFF => (byte)c,
                _ => (byte)'?',
            });
        }
        return octets.ToArray();
    }

    /// <summary>Dimensions d'un JPEG (marqueurs SOF).</summary>
    public static (int Largeur, int Hauteur, int Composantes) LireJpeg(byte[] j)
    {
        if (j.Length < 4 || j[0] != 0xFF || j[1] != 0xD8) throw new InvalidDataException("Image JPEG attendue.");
        var i = 2;
        while (i + 9 < j.Length)
        {
            if (j[i] != 0xFF) { i++; continue; }
            var marqueur = j[i + 1];
            if (marqueur is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                return ((j[i + 7] << 8) | j[i + 8], (j[i + 5] << 8) | j[i + 6], j[i + 9]);
            var longueur = (j[i + 2] << 8) | j[i + 3];
            i += 2 + longueur;
        }
        throw new InvalidDataException("Dimensions JPEG introuvables.");
    }

    private static readonly short[] LargeursNormal =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 761,
        556, 0, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0,
        0, 222, 222, 333, 333, 350, 556, 1000, 333, 1000, 500, 333, 944, 0, 500, 667,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,
    ];

    private static readonly short[] LargeursGras =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584, 761,
        556, 0, 278, 556, 500, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0,
        0, 278, 278, 500, 500, 350, 556, 1000, 333, 1000, 556, 333, 944, 0, 500, 667,
        278, 333, 556, 556, 556, 556, 280, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 611, 556, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        722, 722, 722, 722, 722, 722, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 556, 556, 556, 556, 556, 278, 278, 278, 278,
        611, 611, 611, 611, 611, 611, 611, 584, 611, 611, 611, 611, 611, 556, 611, 556,
    ];
}

public enum Alignement { Gauche, Centre, Droite }

public readonly record struct Couleur(float R, float V, float B)
{
    public static readonly Couleur Noir = new(0, 0, 0);
    public static readonly Couleur Gris = new(0.42f, 0.42f, 0.42f);
    public static readonly Couleur GrisClair = new(0.93f, 0.93f, 0.93f);
    public static readonly Couleur Bordeaux = new(0.545f, 0.118f, 0.176f);
    public static readonly Couleur BordeauxClair = new(0.98f, 0.94f, 0.94f);

    private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    public string Remplissage() => $"{N(R)} {N(V)} {N(B)} rg";
    public string Trait() => $"{N(R)} {N(V)} {N(B)} RG";
}
