using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ICA.Client.Google;

namespace ICA.Desktop.Plateforme;

/// <summary>
/// Secrets (accès Google) chiffrés avec DPAPI : lisibles uniquement par cette session Windows, sur cet ordinateur.
/// Copier le fichier sur un autre PC ne donne donc aucun accès.
/// </summary>
public sealed class CoffreWindows(string chemin) : ICoffreSecret
{
    private readonly object _verrou = new();

    public string? Lire(string nom)
    {
        lock (_verrou) return Charger().GetValueOrDefault(nom);
    }

    public void Ecrire(string nom, string valeur)
    {
        lock (_verrou)
        {
            var d = Charger();
            d[nom] = valeur;
            Sauver(d);
        }
    }

    public void Effacer(string nom)
    {
        lock (_verrou)
        {
            var d = Charger();
            if (d.Remove(nom)) Sauver(d);
        }
    }

    private Dictionary<string, string> Charger()
    {
        try
        {
            if (!File.Exists(chemin)) return [];
            var clair = Dechiffrer(File.ReadAllBytes(chemin));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(clair) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicExceptionDpapi)
        {
            return []; // fichier d'un autre utilisateur ou abîmé : on repart de zéro (il suffira de se reconnecter)
        }
    }

    private void Sauver(Dictionary<string, string> d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
        File.WriteAllBytes(chemin, Chiffrer(JsonSerializer.SerializeToUtf8Bytes(d)));
    }

    // ---------------- DPAPI (crypt32.dll) ----------------

    private sealed class CryptographicExceptionDpapi(string message) : Exception(message);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Taille;
        public IntPtr Donnees;
    }

    private const int CryptprotectUiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob entree, string? description, IntPtr entropie, IntPtr reserve,
        IntPtr invite, int options, ref DataBlob sortie);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob entree, IntPtr description, IntPtr entropie, IntPtr reserve,
        IntPtr invite, int options, ref DataBlob sortie);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memoire);

    private static byte[] Chiffrer(byte[] clair) => Appeler(clair, proteger: true);
    private static byte[] Dechiffrer(byte[] chiffre) => Appeler(chiffre, proteger: false);

    private static byte[] Appeler(byte[] entree, bool proteger)
    {
        var poignee = GCHandle.Alloc(entree, GCHandleType.Pinned);
        var blobEntree = new DataBlob { Taille = entree.Length, Donnees = poignee.AddrOfPinnedObject() };
        var blobSortie = new DataBlob();
        try
        {
            var ok = proteger
                ? CryptProtectData(ref blobEntree, "L'ICA Gestion", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref blobSortie)
                : CryptUnprotectData(ref blobEntree, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref blobSortie);
            if (!ok) throw new CryptographicExceptionDpapi($"DPAPI : erreur {Marshal.GetLastWin32Error()}");
            var sortie = new byte[blobSortie.Taille];
            Marshal.Copy(blobSortie.Donnees, sortie, 0, blobSortie.Taille);
            return sortie;
        }
        finally
        {
            poignee.Free();
            if (blobSortie.Donnees != IntPtr.Zero) LocalFree(blobSortie.Donnees);
        }
    }
}
