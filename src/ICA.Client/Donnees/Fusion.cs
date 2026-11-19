using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.Donnees;

/// <summary>
/// Fusion de deux états complets (cet ordinateur / Google Drive), fiche par fiche.
/// Règle : la version modifiée le plus récemment l'emporte ; une suppression est une modification comme une autre.
/// Les choix sont déterministes, pour que tous les appareils arrivent exactement au même résultat.
/// </summary>
public static class Fusion
{
    public sealed record Bilan(bool LocalModifie, bool DistantAMettreAJour);

    public static Bilan Fusionner(DonneesIca local, DonneesIca distant)
    {
        var modifieLocal = false;
        var aEnvoyer = false;

        void Liste<T>(List<T> loc, List<T> dist) where T : EntiteSynchronisee
        {
            var index = new Dictionary<Guid, int>();
            for (var i = 0; i < loc.Count; i++) index[loc[i].Id] = i;
            var vusADistance = new HashSet<Guid>();

            foreach (var d in dist)
            {
                vusADistance.Add(d.Id);
                if (!index.TryGetValue(d.Id, out var i))
                {
                    loc.Add(d);
                    index[d.Id] = loc.Count - 1;
                    modifieLocal = true;
                    continue;
                }
                var comparaison = Comparer(d, loc[i]);
                if (comparaison > 0) { loc[i] = d; modifieLocal = true; }
                else if (comparaison < 0) aEnvoyer = true;
            }
            if (loc.Any(l => !vusADistance.Contains(l.Id))) aEnvoyer = true;
        }

        Liste(local.Adherents, distant.Adherents);
        Liste(local.Cotisations, distant.Cotisations);
        Liste(local.Activites, distant.Activites);
        Liste(local.AdherentActivites, distant.AdherentActivites);
        Liste(local.ModelesMail, distant.ModelesMail);
        Liste(local.Seances, distant.Seances);
        Liste(local.Messages, distant.Messages ?? []);
        Liste(local.Entreprises, distant.Entreprises ?? []);
        Liste(local.Dons, distant.Dons ?? []);
        Liste(local.Justificatifs, distant.Justificatifs ?? []);
        Liste(local.HistoriqueDons, distant.HistoriqueDons ?? []);
        Liste(local.Association, distant.Association ?? []);

        // Doublons créés séparément sur deux appareils (même cotisation, mêmes modèles fournis).
        var maintenant = DateTimeOffset.UtcNow;
        if (Dedoublonner(local.Cotisations.Where(c => !c.EstSupprime).GroupBy(c => (c.AdherentId, c.Saison)),
                g => g.OrderByDescending(c => c.Statut != StatutCotisation.APayer).ThenBy(c => c.Id).First(), maintenant))
        { modifieLocal = true; aEnvoyer = true; }

        if (Dedoublonner(local.ModelesMail.Where(m => !m.EstSupprime && m.EstSysteme).GroupBy(m => m.Nom),
                g => g.OrderBy(m => m.Id).First(), maintenant))
        { modifieLocal = true; aEnvoyer = true; }

        if (Dedoublonner(local.AdherentActivites.Where(l => !l.EstSupprime).GroupBy(l => (l.AdherentId, l.ActiviteId)),
                g => g.OrderBy(l => l.Id).First(), maintenant))
        { modifieLocal = true; aEnvoyer = true; }

        // Deux ordinateurs ont réservé le même numéro au même moment (avant toute émission) :
        // celui dont l'identifiant est le plus grand cède, il sera renuméroté. Choix identique partout.
        foreach (var g in local.Justificatifs.Where(j => j.Statut is StatutJustificatif.Reserve or StatutJustificatif.Emis)
                     .GroupBy(j => j.Numero).Where(g => g.Count() > 1))
        {
            foreach (var j in g.Where(j => j.Statut == StatutJustificatif.Reserve).OrderBy(j => j.Id).Skip(g.Any(x => x.Statut == StatutJustificatif.Emis) ? 0 : 1))
            {
                j.Statut = StatutJustificatif.Annule;
                j.MotifAnnulation = "Numéro déjà attribué sur un autre ordinateur : renuméroté.";
                j.MarquerModifie(maintenant);
                modifieLocal = true;
                aEnvoyer = true;
            }
        }

        local.EstDemo = false;
        local.VersionSchema = Math.Max(local.VersionSchema, distant.VersionSchema);
        return new Bilan(modifieLocal, aEnvoyer);
    }

    /// <summary>Plus récent d'abord ; à égalité, la version la plus haute ; puis l'identifiant (déterministe).</summary>
    private static int Comparer(EntiteSynchronisee a, EntiteSynchronisee b)
    {
        var c = a.UpdatedAt.CompareTo(b.UpdatedAt);
        if (c != 0) return c;
        c = a.Version.CompareTo(b.Version);
        if (c != 0) return c;
        return (a.DeletedAt is not null).CompareTo(b.DeletedAt is not null);
    }

    private static bool Dedoublonner<TCle, T>(IEnumerable<IGrouping<TCle, T>> groupes, Func<IGrouping<TCle, T>, T> garder, DateTimeOffset maintenant)
        where T : EntiteSynchronisee
    {
        var change = false;
        foreach (var g in groupes.Where(g => g.Count() > 1))
        {
            var garde = garder(g);
            foreach (var autre in g.Where(x => x.Id != garde.Id))
            {
                autre.DeletedAt = maintenant;
                autre.UpdatedAt = maintenant;
                autre.Version++;
                change = true;
            }
        }
        return change;
    }
}
