using System.Text.Json;
using System.Text.Json.Serialization;
using ICA.Contracts;

namespace ICA.Client.Configuration;

/// <summary>
/// Réglages lus depuis appsettings.json (à côté de l'exécutable).
/// Plus tard, une partie viendra du serveur (ARCHITECTURE.md §17 bis).
/// </summary>
public sealed class ParametresApplication
{
    public ParametresSupport Support { get; set; } = new();
    public ParametresCotisations Cotisations { get; set; } = new();
    public ParametresMisesAJour MisesAJour { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ParametresApplication Charger(string chemin)
    {
        try
        {
            if (!File.Exists(chemin)) return new ParametresApplication();
            return JsonSerializer.Deserialize<ParametresApplication>(File.ReadAllText(chemin), Options)
                   ?? new ParametresApplication();
        }
        catch (JsonException)
        {
            return new ParametresApplication();
        }
    }
}

public sealed class ParametresMisesAJour
{
    /// <summary>Dépôt GitHub public où sont publiées les versions (« organisation/dépôt »). Vide = pas de mise à jour automatique.</summary>
    public string Depot { get; set; } = "Ica-Asso/ica-gestion-versions";
}

public sealed class ParametresSupport
{
    /// <summary>Destinataire des rapports « Un problème ? ».</summary>
    public string Email { get; set; } = "";
}

public sealed class ParametresCotisations
{
    public ModeSaison ModeSaison { get; set; } = ModeSaison.Civile;

    /// <summary>Cotisation annuelle d'un adulte (null = à saisir à chaque encaissement).</summary>
    public decimal? TarifAdulte { get; set; } = 25m;

    /// <summary>Cotisation annuelle d'un enfant.</summary>
    public decimal? TarifEnfant { get; set; } = 15m;

    public decimal? TarifPour(TypeAdherent type) => type == TypeAdherent.Enfant ? TarifEnfant : TarifAdulte;
}
