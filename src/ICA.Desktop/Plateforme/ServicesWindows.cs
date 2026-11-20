using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICA.Client.Plateforme;
using ICA.Client.Services;
using ICA.Client.ViewModels;
using ICA.Desktop.Fenetres;
using Microsoft.Win32;

namespace ICA.Desktop.Plateforme;

/// <summary>Implémentation Windows des services demandés par la partie partagée.</summary>
public sealed class ServicesWindows : IServicesPlateforme
{
    /// <summary>Renseigné au démarrage (le contexte a besoin de ce service, et inversement).</summary>
    public ContexteApplication? Contexte { get; set; }

    private static Window? Proprietaire =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public string VersionApplication
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            var plus = v.IndexOf('+');
            return plus > 0 ? v[..plus] : v;
        }
    }

    public string SystemeExploitation => RuntimeInformation.OSDescription;
    public string NomAppareil => Environment.MachineName;

    public bool Confirmer(string titre, string message, string boutonOui, bool danger = false)
        => FenetreMessage.Confirmer(Proprietaire, titre, message, boutonOui, danger);

    public void Informer(string titre, string message)
        => FenetreMessage.Informer(Proprietaire, titre, message);

    public bool EditerAdherent(EditionAdherentViewModel vm)
        => new FenetreEditionAdherent(vm) { Owner = Proprietaire }.ShowDialog() == true;

    public void OuvrirAssistance(bool apresPlantage = false)
    {
        if (Contexte is null) return;
        new FenetreAssistance(new AssistanceViewModel(Contexte, apresPlantage)) { Owner = Proprietaire }.ShowDialog();
    }

    public void ComposerMessage(ComposerMessageViewModel vm)
        => new FenetreComposer(vm) { Owner = Proprietaire }.ShowDialog();

    public bool PlanifierSeance(PlanificationViewModel vm)
        => new FenetrePlanification(vm) { Owner = Proprietaire }.ShowDialog() == true;

    public bool OuvrirFormulaire(FormulaireViewModel vm)
        => new FenetreFormulaire(vm) { Owner = Proprietaire }.ShowDialog() == true;

    public byte[]? ImageEnJpeg(string chemin)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(chemin);
            image.DecodePixelWidth = 1600; // assez de détail même après recadrage d'une photo
            image.EndInit();
            image.Freeze();

            // Fond blanc (les PNG transparents deviendraient noirs en JPEG).
            var visuel = new DrawingVisual();
            using (var dc = visuel.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
            }
            var rendu = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            rendu.Render(visuel);

            // On recadre au plus près du trait : le blanc autour rendait la signature minuscule sur le PDF.
            BitmapSource resultat = rendu;
            if (ZoneEncree(rendu) is { } zone) resultat = new CroppedBitmap(rendu, zone);

            var encodeur = new JpegBitmapEncoder { QualityLevel = 90 };
            encodeur.Frames.Add(BitmapFrame.Create(resultat));
            using var flux = new MemoryStream();
            encodeur.Save(flux);
            return flux.ToArray();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Rectangle qui contient tous les pixels foncés (le trait), avec une petite marge ; null si l'image est vide.</summary>
    private static Int32Rect? ZoneEncree(BitmapSource image)
    {
        int l = image.PixelWidth, h = image.PixelHeight, pas = l * 4;
        var pixels = new byte[pas * h];
        image.CopyPixels(pixels, pas, 0);
        int gauche = l, haut = h, droite = -1, bas = -1;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < l; x++)
            {
                var i = y * pas + x * 4;
                var luminance = (pixels[i] * 0.114 + pixels[i + 1] * 0.587 + pixels[i + 2] * 0.299);
                if (luminance > 190) continue; // papier, ombre légère
                if (x < gauche) gauche = x;
                if (x > droite) droite = x;
                if (y < haut) haut = y;
                if (y > bas) bas = y;
            }
        if (droite < 0) return null;
        var marge = Math.Max(4, Math.Max(droite - gauche, bas - haut) / 30);
        gauche = Math.Max(0, gauche - marge);
        haut = Math.Max(0, haut - marge);
        droite = Math.Min(l - 1, droite + marge);
        bas = Math.Min(h - 1, bas + marge);
        return new Int32Rect(gauche, haut, droite - gauche + 1, bas - haut + 1);
    }

    public void ImprimerFichier(string chemin)
    {
        try
        {
            // Verbe « print » du lecteur PDF installé (Acrobat, etc.).
            Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true, Verb = "print" });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Pas de verbe d'impression (Edge) : on ouvre le PDF, l'impression se fait avec Ctrl + P.
            if (OuvrirLien(chemin))
                Informer("Impression", "Le document s'est ouvert : lancez l'impression avec Ctrl + P.");
        }
    }

    public bool EditerActivite(EditionActiviteViewModel vm)
        => new FenetreActivite(vm) { Owner = Proprietaire }.ShowDialog() == true;

    public void CopierTexte(string texte)
    {
        // Le presse-papiers peut être momentanément occupé par une autre application.
        for (var essai = 0; essai < 3; essai++)
        {
            try { Clipboard.SetText(texte); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(100); }
        }
    }

    public async Task<string> EnvoyerAssistanceAsync(RapportAssistance rapport, bool joindreCapture)
    {
        var destinataire = Contexte?.Parametres.Support.Email ?? "";
        var nomFichier = $"ICA-Assistance-{rapport.Identifiant}.zip";

        // Le rapport (+ capture éventuelle) est préparé en mémoire.
        byte[] archive;
        using (var memoire = new MemoryStream())
        {
            using (var zip = new ZipArchive(memoire, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entree = zip.CreateEntry("rapport.txt");
                using (var w = new StreamWriter(entree.Open(), new System.Text.UTF8Encoding(true)))
                    w.Write(rapport.Complet());

                if (joindreCapture && Application.Current.MainWindow is { } fenetre && CapturerFenetre(fenetre) is { } image)
                {
                    using var flux = zip.CreateEntry("capture.png").Open();
                    image.Save(flux);
                }
            }
            archive = memoire.ToArray();
        }

        // 1. Compte Google connecté : envoi direct, pièce jointe comprise. Rien à faire pour l'utilisatrice.
        if (Contexte?.Gmail is { Disponible: true } gmail && destinataire.Length > 0)
        {
            try
            {
                await gmail.EnvoyerAsync([destinataire], rapport.Objet, rapport.Resume(),
                    new ICA.Client.Google.PieceJointe(nomFichier, "application/zip", archive));
                Contexte.Journal.Info($"Rapport d'assistance envoyé par Gmail {rapport.Identifiant}");
                return "Votre message a été envoyé au responsable technique. Il vous recontactera.\n\n" +
                       $"Code du rapport à rappeler si besoin : {rapport.Identifiant}";
            }
            catch (ICA.Client.Google.SansBoiteGmailException)
            {
                Contexte.Journal.Info("Compte Google sans boîte Gmail : rapport envoyé par la messagerie");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ICA.Client.Google.GoogleDeconnecteException)
            {
                Contexte.Journal.Erreur("Envoi direct du rapport impossible, passage par la messagerie", ex);
            }
        }

        // 2. Sinon : fichier sur le Bureau + message pré-rempli à compléter.
        var bureau = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var cheminZip = Path.Combine(bureau, nomFichier);
        await File.WriteAllBytesAsync(cheminZip, archive);
        Contexte?.Journal.Info($"Rapport d'assistance préparé {rapport.Identifiant}");

        var corps = rapport.Resume() +
                    $"\nPièce jointe : merci de joindre le fichier « {nomFichier} » qui vient d'être placé sur le Bureau.\n";
        var lien = LienCourriel.Creer(Contexte?.Preferences.Messagerie ?? ModeMessagerie.Gmail, [destinataire], rapport.Objet, corps);

        if (OuvrirLien(lien))
        {
            return (Contexte?.Preferences.Messagerie == ModeMessagerie.Gmail
                       ? "Gmail s'est ouvert dans votre navigateur avec le message prêt.\n"
                       : "Votre messagerie s'est ouverte avec le message prêt.\n") +
                   $"Ajoutez la pièce jointe « {nomFichier} » (elle est sur votre Bureau), puis cliquez sur Envoyer.\n\n" +
                   $"Code du rapport à rappeler si besoin : {rapport.Identifiant}";
        }

        CopierTexte(rapport.Resume());
        return "Aucune messagerie n'est configurée sur cet ordinateur.\n" +
               $"Le rapport « {nomFichier} » a été placé sur votre Bureau : envoyez-le à {destinataire}.\n" +
               "Le texte du rapport a aussi été copié : vous pouvez le coller dans un e-mail.";
    }

    private static PngBitmapEncoder? CapturerFenetre(Window fenetre)
    {
        if (fenetre.Content is not FrameworkElement contenu || contenu.ActualWidth < 1) return null;
        var dpi = VisualTreeHelper.GetDpi(contenu);
        var rendu = new RenderTargetBitmap(
            (int)(contenu.ActualWidth * dpi.DpiScaleX), (int)(contenu.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rendu.Render(contenu);
        var encodeur = new PngBitmapEncoder();
        encodeur.Frames.Add(BitmapFrame.Create(rendu));
        return encodeur;
    }

    public string? ChoisirFichierExport(string nomParDefaut)
    {
        var dialogue = new SaveFileDialog
        {
            Title = "Exporter les adhérents",
            FileName = nomParDefaut,
            DefaultExt = ".csv",
            Filter = "Fichier pour Excel (*.csv)|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        var proprietaire = Proprietaire;
        var ok = proprietaire is null ? dialogue.ShowDialog() : dialogue.ShowDialog(proprietaire);
        return ok == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierAOuvrir(string titre, string filtre)
    {
        var dialogue = new OpenFileDialog { Title = titre, Filter = filtre, CheckFileExists = true };
        var proprietaire = Proprietaire;
        return (proprietaire is null ? dialogue.ShowDialog() : dialogue.ShowDialog(proprietaire)) == true ? dialogue.FileName : null;
    }

    public string? ChoisirFichierAEnregistrer(string titre, string nomParDefaut, string filtre)
    {
        var dialogue = new SaveFileDialog { Title = titre, FileName = nomParDefaut, Filter = filtre };
        var proprietaire = Proprietaire;
        return (proprietaire is null ? dialogue.ShowDialog() : dialogue.ShowDialog(proprietaire)) == true ? dialogue.FileName : null;
    }

    public bool OuvrirImport(ImportViewModel vm)
        => new FenetreImport(vm) { Owner = Proprietaire }.ShowDialog() == true;

    public void Imprimer(string titre, IReadOnlyList<LigneImpression> lignes)
    {
        var dialogue = new PrintDialog();
        if (dialogue.ShowDialog() != true) return;

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            PageWidth = dialogue.PrintableAreaWidth,
            PageHeight = dialogue.PrintableAreaHeight,
            PagePadding = new Thickness(48),
            ColumnWidth = double.PositiveInfinity,
        };
        document.Blocks.Add(new Paragraph(new Run(titre)) { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        document.Blocks.Add(new Paragraph(new Run($"{lignes.Count} adhérent(s)")) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 12) });

        var table = new Table { CellSpacing = 0 };
        foreach (var largeur in new[] { 2.2, 0.9, 3.2, 1.5, 2.6, 1.2 })
            table.Columns.Add(new TableColumn { Width = new GridLength(largeur, GridUnitType.Star) });

        var groupe = new TableRowGroup();
        groupe.Rows.Add(Ligne(true, "Nom", "Type", "Adresse", "Téléphone", "E-mail", "Cotisation"));
        var alterne = false;
        foreach (var l in lignes)
        {
            var ligne = Ligne(false, l.Nom, l.Type, l.Adresse, l.Telephone, l.Email, l.Cotisation);
            if (alterne) ligne.Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF4, 0xF1));
            alterne = !alterne;
            groupe.Rows.Add(ligne);
        }
        table.RowGroups.Add(groupe);
        document.Blocks.Add(table);

        dialogue.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, titre);
        Contexte?.Journal.Info($"Impression de {lignes.Count} ligne(s)");
    }

    private static TableRow Ligne(bool entete, params string[] cellules)
    {
        var ligne = new TableRow();
        if (entete) ligne.Background = new SolidColorBrush(Color.FromRgb(0x9E, 0x1B, 0x32));
        foreach (var texte in cellules)
        {
            var paragraphe = new Paragraph(new Run(texte)) { Margin = new Thickness(0) };
            if (entete) { paragraphe.Foreground = Brushes.White; paragraphe.FontWeight = FontWeights.Bold; }
            ligne.Cells.Add(new TableCell(paragraphe) { Padding = new Thickness(6, 4, 6, 4) });
        }
        return ligne;
    }

    /// <summary>
    /// Windows interdit d'écraser un programme en cours d'exécution, mais permet de le renommer :
    /// l'ancien devient « ICA-Gestion.exe.ancien » (effacé au prochain démarrage), le nouveau prend sa place,
    /// puis la nouvelle version est lancée et celle-ci se ferme.
    /// </summary>
    public void InstallerMiseAJour(string nouveauProgramme)
    {
        var actuel = Environment.ProcessPath ?? throw new IOException("Chemin du programme inconnu.");
        var ancien = actuel + ".ancien";
        if (File.Exists(ancien)) File.Delete(ancien);
        File.Move(actuel, ancien);
        try
        {
            File.Copy(nouveauProgramme, actuel);
        }
        catch
        {
            File.Move(ancien, actuel); // retour à l'ancienne version, rien n'est cassé
            throw;
        }
        Process.Start(new ProcessStartInfo(actuel, App.ArgumentApresMiseAJour) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(actuel)! });
        Application.Current.Shutdown();
    }

    public bool OuvrirLien(string lien)
    {
        if (string.IsNullOrWhiteSpace(lien)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(lien) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Contexte?.Journal.Avertissement($"Ouverture impossible ({ex.GetType().Name})");
            return false;
        }
    }
}
