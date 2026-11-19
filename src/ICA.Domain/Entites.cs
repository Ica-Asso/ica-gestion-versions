using System.Text.Json.Serialization;
using ICA.Contracts;

namespace ICA.Domain;

/// <summary>
/// Base de toute donnée synchronisée entre appareils (voir ARCHITECTURE.md §5).
/// Id généré localement (UUID v7) pour pouvoir créer des fiches hors ligne.
/// </summary>
public abstract class EntiteSynchronisee
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Version { get; set; } = 1;
    public DateTimeOffset? DeletedAt { get; set; }

    [JsonIgnore]
    public bool EstSupprime => DeletedAt is not null;

    public void MarquerModifie(DateTimeOffset maintenant)
    {
        UpdatedAt = maintenant;
        Version++;
    }

    public void Supprimer(DateTimeOffset maintenant)
    {
        DeletedAt = maintenant;
        MarquerModifie(maintenant);
    }
}

public sealed class Adherent : EntiteSynchronisee
{
    public string Nom { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string? Adresse { get; set; }
    public string? ComplementAdresse { get; set; }
    public string? CodePostal { get; set; }
    public string? Ville { get; set; }
    public string? Email { get; set; }
    public string? Telephone1 { get; set; }
    public string? Telephone2 { get; set; }
    public DateOnly? DateNaissance { get; set; }
    public TypeAdherent TypeAdherent { get; set; } = TypeAdherent.Adulte;
    public int? AnneeAdhesion { get; set; }

    /// <summary>Pour un enfant : l'adhérent adulte responsable.</summary>
    public Guid? ResponsableId { get; set; }

    public string? Notes { get; set; }

    /// <summary>« Jean DUPONT ».</summary>
    [JsonIgnore]
    public string NomAffiche => $"{Prenom} {Nom.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))}".Trim();

    [JsonIgnore]
    public string Initiales
    {
        get
        {
            var p = Prenom.Trim();
            var n = Nom.Trim();
            var i1 = p.Length > 0 ? char.ToUpperInvariant(p[0]).ToString() : "";
            var i2 = n.Length > 0 ? char.ToUpperInvariant(n[0]).ToString() : "";
            var r = i1 + i2;
            return r.Length > 0 ? r : "?";
        }
    }

    [JsonIgnore]
    public bool EstEnfant => TypeAdherent == TypeAdherent.Enfant;

    public int? Age(DateOnly aujourdHui)
    {
        if (DateNaissance is not { } n) return null;
        var age = aujourdHui.Year - n.Year;
        if (aujourdHui < n.AddYears(age)) age--;
        return age < 0 ? null : age;
    }

    /// <summary>Copie superficielle, utilisée pour éditer sans toucher l'original.</summary>
    public Adherent Copier() => (Adherent)MemberwiseClone();

    /// <summary>Recopie les champs métier (pas les champs techniques).</summary>
    public void CopierChampsMetierDepuis(Adherent source)
    {
        Nom = source.Nom;
        Prenom = source.Prenom;
        Adresse = source.Adresse;
        ComplementAdresse = source.ComplementAdresse;
        CodePostal = source.CodePostal;
        Ville = source.Ville;
        Email = source.Email;
        Telephone1 = source.Telephone1;
        Telephone2 = source.Telephone2;
        DateNaissance = source.DateNaissance;
        TypeAdherent = source.TypeAdherent;
        AnneeAdhesion = source.AnneeAdhesion;
        ResponsableId = source.ResponsableId;
        Notes = source.Notes;
    }
}

public sealed class Cotisation : EntiteSynchronisee
{
    public Guid AdherentId { get; set; }

    /// <summary>« 2026 » (année civile) ou « 2026-2027 » (saison scolaire).</summary>
    public string Saison { get; set; } = "";

    public decimal? Montant { get; set; }
    public StatutCotisation Statut { get; set; } = StatutCotisation.APayer;
    public DateOnly? DatePaiement { get; set; }
    public ModePaiement? ModePaiement { get; set; }
    public string? Notes { get; set; }

    public void MarquerPayee(decimal? montant, ModePaiement mode, DateOnly date, DateTimeOffset maintenant)
    {
        Statut = StatutCotisation.Payee;
        Montant = montant;
        ModePaiement = mode;
        DatePaiement = date;
        MarquerModifie(maintenant);
    }

    public void AnnulerPaiement(DateTimeOffset maintenant)
    {
        Statut = StatutCotisation.APayer;
        DatePaiement = null;
        ModePaiement = null;
        MarquerModifie(maintenant);
    }
}

public sealed class Activite : EntiteSynchronisee
{
    public string Nom { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Couleur de pastille, ex. « #E2457A ».</summary>
    public string? Couleur { get; set; }

    public bool Archivee { get; set; }
}

/// <summary>Un message écrit depuis l'application (historique de la Messagerie).</summary>
public sealed class MessageEnvoye : EntiteSynchronisee
{
    public DateTimeOffset DateEnvoi { get; set; }
    public string Objet { get; set; } = "";
    public string Corps { get; set; } = "";
    /// <summary>Noms des destinataires (« Alice FICTIF, Léo FICTIF… »).</summary>
    public string Destinataires { get; set; } = "";
    public int NombreAdresses { get; set; }
    public string? Modele { get; set; }
    /// <summary>Vrai : parti directement par Gmail. Faux : préparé dans la messagerie de l'ordinateur.</summary>
    public bool EnvoiDirect { get; set; }
}

public sealed class AdherentActivite : EntiteSynchronisee
{
    public Guid AdherentId { get; set; }
    public Guid ActiviteId { get; set; }
}
