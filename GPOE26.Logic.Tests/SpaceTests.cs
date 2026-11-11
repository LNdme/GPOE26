using User.Service;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace GPOE26.Logic.Tests;

/// <summary>Règles des espaces d'établissement (docs/PLAN-ETABLISSEMENT.md, lot 1).</summary>
public class SpaceRulesTests
{
    [Theory]
    [InlineData(2026, 11, 11, "2026-2027")]
    [InlineData(2026, 8, 1, "2026-2027")]   // rentrée : nouvelle année
    [InlineData(2026, 7, 31, "2025-2026")]  // dernier jour de l'année précédente
    [InlineData(2027, 1, 15, "2026-2027")]
    public void L_annee_scolaire_va_d_aout_a_juillet(int year, int month, int day, string expected)
    {
        var (label, startsOn, endsOn) = SpaceRules.SchoolYearOf(new DateOnly(year, month, day));

        Assert.Equal(expected, label);
        Assert.Equal(1, startsOn.Day);
        Assert.Equal(8, startsOn.Month);
        Assert.Equal(new DateOnly(startsOn.Year + 1, 7, 31), endsOn);
        Assert.InRange(new DateOnly(year, month, day), startsOn, endsOn);
    }

    [Theory]
    [InlineData(2026, 7, "2026-0007")]
    [InlineData(2026, 9999, "2026-9999")]
    [InlineData(2026, 12345, "2026-12345")] // jamais tronqué : le matricule reste unique
    public void Le_matricule_genere_est_annee_tiret_numero(int year, int number, string expected) =>
        Assert.Equal(expected, SpaceRules.Matricule(year, number));

    [Fact]
    public void Des_numeros_differents_donnent_des_matricules_differents()
    {
        var matricules = Enumerable.Range(1, 20_000).Select(n => SpaceRules.Matricule(2026, n)).ToList();
        Assert.Equal(matricules.Count, matricules.Distinct().Count());
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData(" Mathématiques ", "Mathématiques")]
    public void La_matiere_vide_veut_dire_toutes_matieres(string? subject, string expected) =>
        Assert.Equal(expected, ClassroomService.SubjectKey(subject));

    [Fact]
    public void La_matiere_est_coupee_a_80_caracteres() =>
        Assert.Equal(80, ClassroomService.SubjectKey(new string('x', 120)).Length);

    [Fact]
    public void Une_annee_saisie_donne_ses_dates()
    {
        Assert.True(SpaceRules.TryParseSchoolYear(" 2027-2028 ", out var year));
        Assert.Equal("2027-2028", year.Label);
        Assert.Equal(new DateOnly(2027, 8, 1), year.StartsOn);
        Assert.Equal(new DateOnly(2028, 7, 31), year.EndsOn);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2027")]
    [InlineData("2027-2029")]   // pas deux années qui se suivent
    [InlineData("2028-2027")]
    [InlineData("27-28")]
    [InlineData("1899-1900")]
    [InlineData("2027-2028-2029")]
    public void Une_annee_mal_ecrite_est_refusee(string? label) =>
        Assert.False(SpaceRules.TryParseSchoolYear(label, out _));
}

/// <summary>Plusieurs enseignants par classe : Cours ne laisse publier que ceux qui y enseignent (lot 2).</summary>
public class MultiTeacherSharingTests
{
    private readonly Guid _me = Guid.NewGuid();
    private readonly Guid _main = Guid.NewGuid();

    [Fact]
    public void Un_co_enseignant_peut_publier_dans_la_classe()
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _main, "Teacher");
        Assert.Equal([classroom.Id], Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
    }

    [Theory]
    [InlineData("Student")]
    [InlineData("Manager")]   // le gestionnaire voit la classe, mais n'y enseigne pas
    public void Un_eleve_ou_un_gestionnaire_ne_publie_pas(string role)
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _me, role);
        Assert.Empty(Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
    }

    [Fact]
    public void Sans_MyRole_le_professeur_principal_reste_le_proprietaire()
    {
        var classroom = new Cours.Service.ClassroomRef(Guid.NewGuid(), _me);
        Assert.Equal([classroom.Id], Cours.Service.UserDirectoryClient.OwnedBy([classroom], _me));
        Assert.Empty(Cours.Service.UserDirectoryClient.OwnedBy([classroom], _main));
    }
}

/// <summary>Import de la liste des élèves (lot 3) : un CSV tel qu'Excel ou LibreOffice l'enregistrent.</summary>
public class StudentImportTests
{
    [Fact]
    public void Un_fichier_Excel_francais_est_lu()
    {
        // Excel en français : séparateur « ; », titres accentués, BOM en tête
        var csv = "﻿N° Matricule;Nom;Prénom(s);Classe;Né(e) le\r\n" +
                  "LYC-001;MBALLA;Jean Paul;Tle C;05/03/2009\r\n" +
                  ";NGO BIYONG;Aïcha;2nde A;\r\n";
        var (rows, error) = StudentImport.Parse(csv);

        Assert.Null(error);
        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal((2, "LYC-001", "MBALLA", "Jean Paul", "Tle C", new DateOnly(2009, 3, 5)),
            (first.Line, first.Matricule, first.LastName, first.FirstName, first.ClassName, first.BirthDate));
        Assert.Null(rows[1].Matricule);           // vide : sera généré
        Assert.Equal("NGO BIYONG", rows[1].LastName);
        Assert.All(rows, r => Assert.Empty(r.Errors));
    }

    [Fact]
    public void Virgules_et_guillemets()
    {
        var csv = "matricule,nom et prénom,classe\n1,\"Essomba, Marie\",\"1ère D\"\n";
        var (rows, _) = StudentImport.Parse(csv);
        Assert.Equal("Essomba, Marie", rows.Single().LastName);
        Assert.Equal("1ère D", rows.Single().ClassName);
    }

    [Fact]
    public void Les_erreurs_sont_signalees_ligne_par_ligne()
    {
        var csv = "matricule;nom;classe;date de naissance\nA1;Mballa;Tle C;31/02/2009\nA1;Essomba;Tle C;\nA2;;Tle C;\n";
        var (rows, error) = StudentImport.Parse(csv);

        Assert.Null(error);
        Assert.Contains(rows[0].Errors, e => e.Contains("date de naissance"));
        Assert.Contains(rows[1].Errors, e => e.Contains("déjà donné ligne 2"));
        Assert.Contains(rows[2].Errors, e => e.Contains("nom manquant"));
    }

    [Theory]
    [InlineData("", "vide")]
    [InlineData("matricule;classe\nA1;Tle C\n", "nom")]
    [InlineData("nom;classe\n", "titres")]
    public void Un_fichier_inutilisable_est_refuse(string csv, string expected)
    {
        var (rows, error) = StudentImport.Parse(csv);
        Assert.Empty(rows);
        Assert.Contains(expected, error);
    }

    [Theory]
    [InlineData("N° Matricule", "n matricule")]
    [InlineData("  Prénom(s) ", "prenom s")]
    [InlineData("Né(e) le", "ne e le")]
    public void Les_titres_sont_compares_sans_accents(string title, string expected) =>
        Assert.Equal(expected, StudentImport.Normalize(title));
}

/// <summary>Codes personnels (lot 3).</summary>
public class AccessCodeRulesTests
{
    [Fact]
    public void Un_code_personnel_a_huit_caracteres_sans_ambiguite()
    {
        var code = CodeGenerator.AccessCode();
        Assert.Matches("^GP-[A-HJ-KM-NP-Z2-9]{4}-[A-HJ-KM-NP-Z2-9]{4}$", code);
        Assert.DoesNotContain('O', code[3..]);
        Assert.DoesNotContain('0', code);
    }

    [Theory]
    [InlineData(User.Model.AccessCodeKind.Student, User.Model.UserRole.Student, true)]
    [InlineData(User.Model.AccessCodeKind.Student, User.Model.UserRole.Teacher, false)]
    [InlineData(User.Model.AccessCodeKind.Teacher, User.Model.UserRole.Teacher, true)]
    [InlineData(User.Model.AccessCodeKind.Teacher, User.Model.UserRole.Student, false)]
    [InlineData(User.Model.AccessCodeKind.Owner, User.Model.UserRole.Parent, true)]
    [InlineData(User.Model.AccessCodeKind.Owner, User.Model.UserRole.Student, false)]
    public void Le_code_va_avec_le_bon_type_de_compte(User.Model.AccessCodeKind kind, User.Model.UserRole role, bool expected) =>
        Assert.Equal(expected, AccessCodeService.RoleFits(kind, role));
}

/// <summary>Import Excel, Word et CSV, parcours, e-mail et colonnes en plus (docs/PLAN-ETABLISSEMENT-2.md, lot 9).</summary>
public class ImportFileTests
{
    private static List<IReadOnlyList<string>> Table(params string[][] rows) => rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList();

    [Fact]
    public void Les_lignes_de_titre_au_dessus_des_colonnes_sont_ignorees()
    {
        var (rows, error) = StudentImport.ParseTable(Table(
            ["Liste des élèves — Terminale C", "", ""],
            [],
            ["Matricule", "Nom", "Classe"],
            ["A1", "MBALLA", "Tle C"]));
        Assert.Null(error);
        Assert.Equal(("A1", "MBALLA", "Tle C", 4), (rows.Single().Matricule, rows.Single().LastName, rows.Single().ClassName, rows.Single().Line));
    }

    [Fact]
    public void Parcours_email_et_autres_colonnes()
    {
        var (rows, _) = StudentImport.ParseTable(Table(
            ["Nom complet", "Parcours", "Adresse e-mail", "Téléphone", "Nom du parent"],
            ["Essomba Marie", "Data 2026", "Marie.E@Exemple.cm", "699 00 00 00", ""],
            ["Fouda Paul", "Data 2026", "pas-un-mail", "", "M. Fouda"]));

        Assert.Equal("Data 2026", rows[0].ClassName);
        Assert.Equal("marie.e@exemple.cm", rows[0].Email);
        Assert.Equal(new Dictionary<string, string> { ["Téléphone"] = "699 00 00 00" }, rows[0].Extra);   // cellule vide non gardée
        Assert.Empty(rows[0].Errors);
        Assert.Contains(rows[1].Errors, e => e.Contains("e-mail illisible"));
        Assert.Equal("M. Fouda", rows[1].Extra["Nom du parent"]);
    }

    [Fact]
    public void Le_csv_remis_en_tableau_puis_en_csv_ne_perd_rien()
    {
        var table = Table(["Nom", "Remarque"], ["MBALLA", "dit \"Jojo\"; délégué"]);
        var back = StudentImport.SplitCsv(StudentImport.ToCsv(table));
        Assert.Equal(table.Select(r => string.Join("|", r)), back.Select(r => string.Join("|", r)));
    }

    [Theory]
    [InlineData("liste.xlsx", ImportFileReader.Kind.Excel)]
    [InlineData("Liste.DOCX", ImportFileReader.Kind.Word)]
    [InlineData("liste.csv", ImportFileReader.Kind.Csv)]
    [InlineData("liste.xls", ImportFileReader.Kind.Unknown)]
    [InlineData("liste.doc", ImportFileReader.Kind.Unknown)]
    public void Le_format_vient_de_l_extension(string name, ImportFileReader.Kind kind) =>
        Assert.Equal(kind, ImportFileReader.KindOf(name));

    [Fact]
    public void Un_ancien_format_est_refuse_avec_une_explication()
    {
        var (table, error) = ImportFileReader.Read(new MemoryStream([1, 2, 3]), "liste.xls");
        Assert.Empty(table);
        Assert.Contains("Enregistrer sous", error);
    }

    [Fact]
    public void Un_fichier_abime_est_refuse_sans_planter()
    {
        var (_, error) = ImportFileReader.Read(new MemoryStream("pas un classeur"u8.ToArray()), "liste.xlsx");
        Assert.Contains("illisible", error);
    }

    [Fact]
    public void Un_classeur_Excel_est_lu_comme_Excel_l_affiche()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Tle C");
        sheet.Cell(1, 1).Value = "Lycée de démonstration — liste officielle";
        sheet.Range(1, 1, 1, 5).Merge();                                  // titre fusionné au-dessus
        string[] titles = ["N° Matricule", "Nom", "Prénom(s)", "Né(e) le", "Téléphone"];
        for (var i = 0; i < titles.Length; i++) sheet.Cell(2, i + 1).Value = titles[i];
        sheet.Cell(3, 1).Value = 123; sheet.Cell(3, 1).Style.NumberFormat.Format = "00000";   // matricule à zéros devant
        sheet.Cell(3, 2).Value = "MBALLA"; sheet.Cell(3, 3).Value = "Jean Paul";
        sheet.Cell(3, 4).Value = new DateTime(2009, 3, 5);                // vraie date Excel
        sheet.Cell(3, 5).Value = "699 11 22 33";
        using var file = new MemoryStream();
        workbook.SaveAs(file);
        file.Position = 0;

        var (table, error) = ImportFileReader.Read(file, "liste.xlsx");
        Assert.Null(error);
        var (rows, parseError) = StudentImport.ParseTable(table);
        Assert.Null(parseError);
        var r = rows.Single();
        Assert.Equal(("00123", "MBALLA", "Jean Paul", new DateOnly(2009, 3, 5)), (r.Matricule, r.LastName, r.FirstName, r.BirthDate!.Value));
        Assert.Equal("699 11 22 33", r.Extra["Téléphone"]);
    }

    [Fact]
    public void Le_tableau_d_un_document_Word_est_lu_cellules_fusionnees_comprises()
    {
        using var file = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            static W.TableCell Cell(string text, W.TableCellProperties? props = null) =>
                new(props ?? new W.TableCellProperties(), new W.Paragraph(new W.Run(new W.Text(text))));
            var table = new W.Table(
                new W.TableRow(Cell("Matricule"), Cell("Nom et prénoms"), Cell("Classe")),
                new W.TableRow(Cell("TC-01"), Cell("MBALLA Jean"),
                    Cell("Terminale C", new W.TableCellProperties(new W.VerticalMerge { Val = W.MergedCellValues.Restart }))),
                new W.TableRow(Cell("TC-02"), Cell("NGO Aïcha"),
                    Cell("", new W.TableCellProperties(new W.VerticalMerge()))));     // la classe continue celle du dessus
            main.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("Liste de la Terminale C"))), table));
        }
        file.Position = 0;

        var (tableRows, error) = ImportFileReader.Read(file, "liste.docx");
        Assert.Null(error);
        var (rows, _) = StudentImport.ParseTable(tableRows);
        Assert.Equal([("TC-01", "MBALLA Jean", "Terminale C"), ("TC-02", "NGO Aïcha", "Terminale C")],
            rows.Select(r => (r.Matricule, r.LastName, r.ClassName)).ToList());
    }

    [Fact]
    public void Un_document_Word_sans_tableau_est_explique()
    {
        using var file = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(file, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            doc.AddMainDocumentPart().Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text("MBALLA Jean, Tle C")))));
        }
        file.Position = 0;
        Assert.Contains("Aucun tableau", ImportFileReader.Read(file, "liste.docx").Error);
    }
}
