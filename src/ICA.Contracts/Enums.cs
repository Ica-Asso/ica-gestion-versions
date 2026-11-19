namespace ICA.Contracts;

/// <summary>Type d'adhérent.</summary>
public enum TypeAdherent
{
    Adulte = 0,
    Enfant = 1,
}

/// <summary>Statut d'une cotisation pour une saison.</summary>
public enum StatutCotisation
{
    APayer = 0,
    Payee = 1,
    Exoneree = 2,
}

/// <summary>Mode de paiement d'une cotisation.</summary>
public enum ModePaiement
{
    Especes = 0,
    Cheque = 1,
    Virement = 2,
    Autre = 3,
}

/// <summary>Découpage des saisons de cotisation.</summary>
public enum ModeSaison
{
    /// <summary>Janvier → décembre ("2026").</summary>
    Civile = 0,

    /// <summary>Septembre → août ("2026-2027").</summary>
    Scolaire = 1,
}
