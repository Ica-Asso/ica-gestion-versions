using ICA.Contracts;
using ICA.Domain;

namespace ICA.Client.Donnees;

/// <summary>
/// Jeu de données 100 % fictif pour la démonstration (CDC §73).
/// Aucun nom ni coordonnée réels.
/// </summary>
public static class DonneesDemo
{
    private sealed record Personne(
        string Prenom, string Nom, string Adresse, string Cp, string Ville,
        int AnneeNaissance, int? DecalageAnniversaire, int Mois, int Jour,
        bool Paye, ModePaiement Mode, int DecalagePaiement, string[] Activites,
        string? Responsable = null, int AdhesionDepuis = 2022);

    public static DonneesIca Creer(DateOnly aujourdHui, string saison, ICA.Client.Configuration.ParametresCotisations tarifs)
    {
        var maintenant = DateTimeOffset.Now;
        var donnees = new DonneesIca { ModelesMail = ModelesParDefaut.Creer() };

        var activites = new Dictionary<string, Activite>
        {
            ["Couture"] = new() { Nom = "Couture", Description = "Atelier couture du mardi", Couleur = "#E2457A" },
            ["Informatique"] = new() { Nom = "Informatique", Description = "Initiation ordinateur et tablette", Couleur = "#2B9ED8" },
            ["Cuisine"] = new() { Nom = "Cuisine", Description = "Recettes partagées", Couleur = "#F29F05" },
            ["Jardinage"] = new() { Nom = "Jardinage", Description = "Jardin partagé", Couleur = "#3FAE5A" },
            ["Théâtre"] = new() { Nom = "Théâtre enfants", Description = "Le mercredi après-midi", Couleur = "#8E5BD9" },
        };
        donnees.Activites.AddRange(activites.Values);

        // DecalageAnniversaire : jours à partir d'aujourd'hui (pour que la démo affiche toujours des anniversaires).
        var personnes = new Personne[]
        {
            new("Jean", "Dupont", "12 rue des Fleurs", "23000", "Guéret", 1968, null, 3, 14, true, ModePaiement.Cheque, 49, ["Couture", "Informatique"], AdhesionDepuis: 2021),
            new("Léa", "Dupont", "12 rue des Fleurs", "23000", "Guéret", aujourdHui.Year - 9, null, 6, 2, true, ModePaiement.Cheque, 49, ["Théâtre"], Responsable: "Jean Dupont", AdhesionDepuis: 2023),
            new("Sophie", "Bernard", "4 place du Marché", "23380", "Ajain", 1975, null, 11, 21, true, ModePaiement.Cheque, 16, ["Cuisine"]),
            new("Camille", "Roux", "8 allée des Tilleuls", "23000", "Guéret", 1982, 2, 1, 1, true, ModePaiement.Especes, 3, ["Couture"]),
            new("Claire", "Petit", "3 rue Pasteur", "23300", "La Souterraine", 1984, null, 2, 9, true, ModePaiement.Virement, 30, ["Jardinage"]),
            new("Lucas", "Petit", "3 rue Pasteur", "23300", "La Souterraine", aujourdHui.Year - 11, 5, 1, 1, true, ModePaiement.Virement, 30, ["Théâtre"], Responsable: "Claire Petit"),
            new("Paul", "Martin", "15 route de Limoges", "23300", "La Souterraine", 1959, 7, 1, 1, true, ModePaiement.Virement, 10, ["Informatique", "Jardinage"], AdhesionDepuis: 2019),
            new("Marie", "Leroy", "21 avenue de la Gare", "23000", "Guéret", 1990, null, 7, 30, false, default, 0, ["Cuisine"]),
            new("Thomas", "Moreau", "6 impasse du Moulin", "23000", "Sainte-Feyre", 1987, null, 9, 12, false, default, 0, ["Informatique"], AdhesionDepuis: 2024),
            new("Sophie", "Girard", "2 chemin des Prés", "23320", "Saint-Vaury", 1980, null, 4, 3, true, ModePaiement.Especes, 25, ["Couture"]),
            new("Hugo", "Girard", "2 chemin des Prés", "23320", "Saint-Vaury", aujourdHui.Year - 8, null, 10, 6, false, default, 0, ["Théâtre"], Responsable: "Sophie Girard", AdhesionDepuis: 2025),
            new("Nathalie", "Fournier", "9 rue Victor Hugo", "23320", "Saint-Vaury", 1972, null, 5, 18, false, default, 0, ["Couture", "Cuisine"]),
            new("Éric", "Lambert", "1 place Bonnyaud", "23000", "Guéret", 1965, null, 12, 2, true, ModePaiement.Cheque, 40, ["Jardinage"], AdhesionDepuis: 2020),
            new("Isabelle", "Faure", "17 rue de la Liberté", "23200", "Aubusson", 1978, null, 8, 25, true, ModePaiement.Cheque, 38, ["Couture"]),
            new("Julien", "Mercier", "5 rue des Écoles", "23400", "Bourganeuf", 1993, null, 2, 27, true, ModePaiement.Virement, 22, ["Informatique"], AdhesionDepuis: 2025),
            new("Chloé", "Mercier", "5 rue des Écoles", "23400", "Bourganeuf", aujourdHui.Year - 10, null, 9, 9, true, ModePaiement.Virement, 22, ["Théâtre"], Responsable: "Julien Mercier", AdhesionDepuis: 2025),
            new("Monique", "Blanc", "11 rue du Château", "23000", "Guéret", 1951, null, 10, 15, true, ModePaiement.Especes, 55, ["Couture", "Cuisine"], AdhesionDepuis: 2018),
            new("Gérard", "Chevalier", "28 route de Montluçon", "23000", "Saint-Sulpice-le-Guérétois", 1956, null, 6, 21, true, ModePaiement.Cheque, 44, ["Jardinage"], AdhesionDepuis: 2018),
            new("Nadia", "Benali", "7 rue Ernest Lavisse", "23000", "Guéret", 1988, null, 3, 30, true, ModePaiement.Especes, 12, ["Cuisine"]),
            new("Antoine", "Garnier", "14 rue de Bellevue", "23200", "Aubusson", 1970, null, 7, 4, true, ModePaiement.Cheque, 35, ["Informatique"]),
            new("Élodie", "Roussel", "19 avenue Fayolle", "23000", "Guéret", 1996, null, 1, 17, true, ModePaiement.Virement, 8, ["Couture"], AdhesionDepuis: 2024),
            new("Nicolas", "Henry", "30 rue de Pommeil", "23000", "Guéret", 1983, null, 11, 4, true, ModePaiement.Especes, 18, ["Jardinage"]),
        };

        var parNom = new Dictionary<string, Adherent>();
        var numero = 1;
        foreach (var p in personnes)
        {
            DateOnly naissance;
            if (p.DecalageAnniversaire is { } d)
            {
                var jour = aujourdHui.AddDays(d);
                naissance = new DateOnly(p.AnneeNaissance, jour.Month, Math.Min(jour.Day, DateTime.DaysInMonth(p.AnneeNaissance, jour.Month)));
            }
            else
            {
                naissance = new DateOnly(p.AnneeNaissance, p.Mois, p.Jour);
            }

            var estEnfant = p.Responsable is not null;
            var a = new Adherent
            {
                Prenom = p.Prenom,
                Nom = p.Nom,
                Adresse = p.Adresse,
                CodePostal = p.Cp,
                Ville = p.Ville,
                DateNaissance = naissance,
                TypeAdherent = estEnfant ? TypeAdherent.Enfant : TypeAdherent.Adulte,
                AnneeAdhesion = p.AdhesionDepuis,
                Email = estEnfant ? null : $"{Texte.PourRecherche(p.Prenom).Replace(' ', '-')}.{Texte.PourRecherche(p.Nom).Replace(' ', '-')}@example.com",
                Telephone1 = estEnfant ? null : $"+33600000{numero:000}",
                ResponsableId = estEnfant ? parNom[p.Responsable!].Id : null,
            };
            parNom[$"{p.Prenom} {p.Nom}"] = a;
            donnees.Adherents.Add(a);
            numero++;

            var montant = tarifs.TarifPour(a.TypeAdherent);
            var c = new Cotisation { AdherentId = a.Id, Saison = saison, Montant = montant };
            if (p.Paye) c.MarquerPayee(montant, p.Mode, aujourdHui.AddDays(-p.DecalagePaiement), maintenant);
            donnees.Cotisations.Add(c);

            foreach (var nomActivite in p.Activites)
                donnees.AdherentActivites.Add(new AdherentActivite { AdherentId = a.Id, ActiviteId = activites[nomActivite].Id });
        }

        // Les responsables doivent être créés avant leurs enfants dans la liste ci-dessus.
        donnees.Seances = CreerPlanning(donnees.Activites, aujourdHui);
        return donnees;
    }

    /// <summary>Ateliers hebdomadaires sur ~3 mois + quelques événements, autour d'aujourd'hui.</summary>
    public static List<Seance> CreerPlanning(IReadOnlyList<Activite> activites, DateOnly aujourdHui)
    {
        var seances = new List<Seance>();
        Activite? Trouver(string nom) => activites.FirstOrDefault(a => a.Nom.StartsWith(nom, StringComparison.Ordinal));

        void Hebdo(string nom, DayOfWeek jour, int hD, int mD, int hF, int mF, string lieu)
        {
            if (Trouver(nom) is not { } activite) return;
            var debut = aujourdHui.AddDays(-28);
            while (debut.DayOfWeek != jour) debut = debut.AddDays(1);
            var serie = Guid.CreateVersion7();
            foreach (var d in RegleSeance.ChaqueSemaine(debut, FinDeSaison(aujourdHui)))
                seances.Add(new Seance
                {
                    ActiviteId = activite.Id, Date = d, SerieId = serie, Lieu = lieu,
                    HeureDebut = new TimeOnly(hD, mD), HeureFin = new TimeOnly(hF, mF),
                });
        }

        // L'ICA se retrouve tous les mercredis de 14h à 17h, de janvier à décembre.
        Hebdo("Couture", DayOfWeek.Wednesday, 14, 0, 17, 0, "Salle de L'ICA");
        Hebdo("Informatique", DayOfWeek.Wednesday, 14, 0, 17, 0, "Salle de L'ICA");
        Hebdo("Théâtre", DayOfWeek.Wednesday, 14, 0, 17, 0, "Salle de L'ICA");

        // Une séance annulée, pour montrer l'affichage.
        var annulable = seances.Where(s => s.Date > aujourdHui.AddDays(7)).OrderBy(s => s.Date).FirstOrDefault();
        if (annulable is not null) annulable.Annulee = true;

        seances.Add(new Seance
        {
            Titre = "Assemblée générale", Date = aujourdHui.AddDays(20), HeureDebut = new TimeOnly(18, 0),
            HeureFin = new TimeOnly(20, 0), Lieu = "Salle polyvalente", Notes = "Prévoir le pot de l'amitié",
        });
        var noel = new DateOnly(aujourdHui.Month == 12 && aujourdHui.Day > 20 ? aujourdHui.Year + 1 : aujourdHui.Year, 12, 19);
        seances.Add(new Seance
        {
            Titre = "Repas de fin d'année", Date = noel, HeureDebut = new TimeOnly(12, 0), Lieu = "Salle polyvalente",
        });
        return seances;
    }

    /// <summary>31 décembre de l'année en cours (ou de l'année suivante si on est déjà fin décembre).</summary>
    public static DateOnly FinDeSaison(DateOnly aujourdHui) =>
        new(aujourdHui.Month == 12 && aujourdHui.Day > 15 ? aujourdHui.Year + 1 : aujourdHui.Year, 12, 31);
}
