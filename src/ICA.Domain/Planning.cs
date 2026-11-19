using System.Globalization;

namespace ICA.Domain;

/// <summary>
/// Une date prévue au calendrier : séance d'une activité, ou événement ponctuel (AG, repas…).
/// Une répétition hebdomadaire crée une séance par semaine (même SerieId), ce qui permet
/// d'annuler ou de déplacer une seule date, et se synchronise simplement entre appareils.
/// </summary>
public sealed class Seance : EntiteSynchronisee
{
    /// <summary>Activité concernée ; null pour un événement ponctuel.</summary>
    public Guid? ActiviteId { get; set; }

    /// <summary>Nom de l'événement ponctuel (ou précision, ex. « Spécial Noël »).</summary>
    public string? Titre { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly? HeureDebut { get; set; }
    public TimeOnly? HeureFin { get; set; }
    public string? Lieu { get; set; }
    public string? Notes { get; set; }
    public bool Annulee { get; set; }

    /// <summary>Séances créées ensemble par une répétition.</summary>
    public Guid? SerieId { get; set; }

    public Seance Copier() => (Seance)MemberwiseClone();
}

public static class RegleSeance
{
    public const int MaxRepetitions = 60;

    public static IReadOnlyList<ErreurValidation> Valider(Seance s)
    {
        var erreurs = new List<ErreurValidation>();
        if (s.ActiviteId is null && string.IsNullOrWhiteSpace(s.Titre))
            erreurs.Add(new("Titre", "Choisissez une activité ou donnez un nom à l'événement."));
        if (s.HeureDebut is { } d && s.HeureFin is { } f && f <= d)
            erreurs.Add(new("HeureFin", "L'heure de fin doit être après l'heure de début."));
        return erreurs;
    }

    /// <summary>Dates d'une répétition hebdomadaire, du premier jour jusqu'à la date de fin incluse.</summary>
    public static IReadOnlyList<DateOnly> ChaqueSemaine(DateOnly debut, DateOnly fin)
    {
        var dates = new List<DateOnly>();
        for (var d = debut; d <= fin && dates.Count < MaxRepetitions; d = d.AddDays(7))
            dates.Add(d);
        return dates;
    }

    /// <summary>« 14h00 », « 14:00 », « 14h », « 9h30 » → heure ; vide → null.</summary>
    public static bool TryLireHeure(string? saisie, out TimeOnly? heure)
    {
        heure = null;
        if (string.IsNullOrWhiteSpace(saisie)) return true;
        var t = saisie.Trim().ToLowerInvariant().Replace('h', ':').Replace('.', ':').Replace(" ", "");
        if (t.EndsWith(':')) t += "00";
        if (!t.Contains(':') && int.TryParse(t, out var seule) && seule is >= 0 and <= 23) t = seule + ":00";
        if (TimeOnly.TryParseExact(t, ["H:mm", "HH:mm", "H:m"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var h))
        {
            heure = h;
            return true;
        }
        return false;
    }

    public static string AfficherHeure(TimeOnly? h) =>
        h is { } v ? (v.Minute == 0 ? $"{v.Hour}h" : $"{v.Hour}h{v.Minute:00}") : "";

    public static string Horaire(Seance s) => (s.HeureDebut, s.HeureFin) switch
    {
        ({ } d, { } f) => $"{AfficherHeure(d)} – {AfficherHeure(f)}",
        ({ } d, null) => AfficherHeure(d),
        _ => "Journée",
    };
}
