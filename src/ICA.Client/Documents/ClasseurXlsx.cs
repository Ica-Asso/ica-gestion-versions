using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace ICA.Client.Documents;

/// <summary>Écriture d'un classeur Excel (.xlsx) simple, sans dépendance : une ou plusieurs feuilles de texte et de nombres.</summary>
public sealed class ClasseurXlsx
{
    private readonly List<(string Nom, List<object?[]> Lignes)> _feuilles = [];

    public void AjouterFeuille(string nom, IEnumerable<object?[]> lignes)
    {
        var propre = new string(nom.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        _feuilles.Add((propre.Length > 31 ? propre[..31] : propre, lignes.ToList()));
    }

    public byte[] Octets()
    {
        using var flux = new MemoryStream();
        using (var zip = new ZipArchive(flux, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Ecrire(string chemin, string contenu)
            {
                using var w = new StreamWriter(zip.CreateEntry(chemin).Open(), new UTF8Encoding(false));
                w.Write(contenu);
            }

            var feuilles = Enumerable.Range(1, _feuilles.Count).ToList();
            Ecrire("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                string.Concat(feuilles.Select(i => $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");
            Ecrire("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Ecrire("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                string.Concat(feuilles.Select(i => $"<sheet name=\"{SecurityElement.Escape(_feuilles[i - 1].Nom)}\" sheetId=\"{i}\" r:id=\"rId{i}\"/>")) +
                "</sheets></workbook>");
            Ecrire("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(feuilles.Select(i => $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>")) +
                $"<Relationship Id=\"rId{_feuilles.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            // Styles : 0 normal, 1 en-tête gras, 2 montant « 0,00 € », 3 date.
            Ecrire("xl/styles.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\\ &quot;€&quot;\"/></numFmts>" +
                "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
                "<borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
                "<cellXfs count=\"4\"><xf/><xf fontId=\"1\" applyFont=\"1\"/><xf numFmtId=\"164\" applyNumberFormat=\"1\"/><xf numFmtId=\"14\" applyNumberFormat=\"1\"/></cellXfs>" +
                "</styleSheet>");

            for (var f = 0; f < _feuilles.Count; f++)
            {
                var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
                var lignes = _feuilles[f].Lignes;
                for (var r = 0; r < lignes.Count; r++)
                {
                    sb.Append($"<row r=\"{r + 1}\">");
                    for (var c = 0; c < lignes[r].Length; c++)
                    {
                        var reference = Colonne(c) + (r + 1);
                        var style = r == 0 ? " s=\"1\"" : "";
                        switch (lignes[r][c])
                        {
                            case null: break;
                            case decimal m: sb.Append($"<c r=\"{reference}\" s=\"2\"><v>{m.ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                            case int n: sb.Append($"<c r=\"{reference}\"{style}><v>{n}</v></c>"); break;
                            case DateOnly d: sb.Append($"<c r=\"{reference}\" s=\"3\"><v>{d.ToDateTime(TimeOnly.MinValue).ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                            case var v: sb.Append($"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{SecurityElement.Escape(v.ToString() ?? "")}</t></is></c>"); break;
                        }
                    }
                    sb.Append("</row>");
                }
                sb.Append("</sheetData></worksheet>");
                Ecrire($"xl/worksheets/sheet{f + 1}.xml", sb.ToString());
            }
        }
        return flux.ToArray();
    }

    private static string Colonne(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }
}
