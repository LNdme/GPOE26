using System.Reflection;
using Cours.DTOs;

namespace GPOE26.Tests;

/// <summary>
/// L'étanchéité du suivi, vérifiée sur la forme des réponses plutôt que sur l'interface.
///
/// La garantie du produit est simple à énoncer et facile à perdre : ce qu'un élève rédige
/// pour son cours remonte à son parent et à son professeur — c'est du travail scolaire —
/// mais ce qu'il dit au répétiteur ne remonte jamais. C'est là qu'il écrit « je n'ai rien
/// compris » ; s'il apprend que ces mots sont lus, il cessera de les écrire, et l'outil
/// perdra ce qui le rend utile.
///
/// La suite de conformité vérifie cela sur le JSON réellement renvoyé, mais elle
/// n'interroge que le harness. La vue de classe vit dans le service Cours, hors de sa
/// portée. Ce fichier la couvre autrement : en énumérant, par réflexion, tout champ de
/// texte que la réponse peut porter.
///
/// L'intérêt n'est pas de prouver qu'il n'y a pas de fuite aujourd'hui — une lecture
/// suffit. C'est qu'un champ ajouté dans six mois fasse échouer un test au lieu de
/// passer inaperçu.
/// </summary>
public class SuiviLeakTests
{
    /// <summary>
    /// Les seuls textes que la vue de classe a le droit de porter.
    ///
    /// Deux titres, l'un de section, l'autre de cours. Aucun des deux n'est écrit par
    /// l'élève : ils viennent du cours lui-même.
    /// </summary>
    private static readonly HashSet<string> AllowedInClassOverview =
    [
        $"{nameof(ClassWeakSpotDto)}.{nameof(ClassWeakSpotDto.Heading)}",
        $"{nameof(ClassWeakSpotDto)}.{nameof(ClassWeakSpotDto.CourseTitle)}",
    ];

    [Fact]
    public void La_vue_de_classe_ne_porte_aucun_texte_libre()
    {
        var found = TextFields(typeof(ClassOverviewDto));

        // Égalité stricte dans les deux sens. Un champ de trop est une fuite possible ;
        // un champ manquant veut dire que la liste d'exceptions a survécu au champ
        // qu'elle justifiait, et qu'elle protège désormais quelque chose d'autre.
        Assert.Equal(
            AllowedInClassOverview.OrderBy(f => f),
            found.OrderBy(f => f));
    }

    /// <summary>
    /// Le pendant : la vue par élève, elle, porte bien ce que l'élève a rédigé.
    ///
    /// Ce test dit l'inverse du précédent et c'est volontaire — sans lui, on pourrait
    /// satisfaire toute cette suite en cessant de remonter le travail scolaire, ce qui
    /// viderait l'espace parent de son contenu tout en gardant les tests verts.
    /// </summary>
    [Fact]
    public void La_vue_par_eleve_porte_bien_le_travail_redige()
    {
        var found = TextFields(typeof(ChildCourseDetailDto));

        Assert.Contains($"{nameof(WrittenAnswerDto)}.{nameof(WrittenAnswerDto.Answer)}", found);
    }

    /// <summary>
    /// Le résumé d'un élève — la réponse à « a-t-il travaillé cette semaine ».
    ///
    /// Il porte des titres, les notions ratées, et la réponse de synthèse que l'élève a
    /// rédigée. Cette dernière est là volontairement depuis la phase 2b : c'est ce qui
    /// permet de voir s'il a compris l'essence de son cours, et c'est du travail
    /// scolaire, pas une confidence.
    ///
    /// Ce même endpoint sert désormais l'enseignant. La liste ci-dessous est donc aussi
    /// la réponse à « que voit un professeur d'un de ses élèves » : des titres, des
    /// notions, et ce que l'élève a rendu. Rien de ce qu'il a dit au répétiteur.
    /// </summary>
    [Fact]
    public void Le_resume_d_un_eleve_porte_des_titres_et_ce_qu_il_a_rendu()
    {
        var expected = new[]
        {
            $"{nameof(ChildCourseProgressDto)}.{nameof(ChildCourseProgressDto.Title)}",
            $"{nameof(ChildCourseProgressDto)}.{nameof(ChildCourseProgressDto.Subject)}",
            $"{nameof(ChildCourseProgressDto)}.{nameof(ChildCourseProgressDto.CurrentStepTitle)}",
            $"{nameof(ChildCourseProgressDto)}.{nameof(ChildCourseProgressDto.WeakHeadings)}",
            $"{nameof(WrittenAnswerDto)}.{nameof(WrittenAnswerDto.StepTitle)}",
            $"{nameof(WrittenAnswerDto)}.{nameof(WrittenAnswerDto.Answer)}",
            $"{nameof(WrittenAnswerDto)}.{nameof(WrittenAnswerDto.CorrectionSummary)}",
        };

        Assert.Equal(expected.OrderBy(f => f), TextFields(typeof(ChildOverviewDto)).OrderBy(f => f));
    }

    /// <summary>
    /// Le garde-fou du garde-fou : si le parcours de types cessait de descendre, les
    /// tests ci-dessus passeraient sur une liste vide sans rien vérifier.
    /// </summary>
    [Fact]
    public void Le_parcours_descend_bien_dans_les_types_imbriques()
    {
        var found = TextFields(typeof(ChildOverviewDto));

        // Deux niveaux sous la racine, et à travers une liste : ChildOverviewDto →
        // List<ChildCourseProgressDto> → WrittenAnswerDto.
        Assert.Contains($"{nameof(WrittenAnswerDto)}.{nameof(WrittenAnswerDto.Answer)}", found);

        // Et à travers une liste de chaînes, que la première version manquait.
        Assert.Contains($"{nameof(ChildCourseProgressDto)}.{nameof(ChildCourseProgressDto.WeakHeadings)}", found);
    }

    // ── Parcours du graphe de types ───────────────────────────────────────────

    /// <summary>
    /// Tout champ de type string atteignable depuis <paramref name="root"/>, nommé
    /// « Type.Propriété ».
    ///
    /// On descend dans les listes et dans les records imbriqués : une fuite passe
    /// rarement par le premier niveau, où quelqu'un l'aurait vue.
    /// </summary>
    private static HashSet<string> TextFields(Type root)
    {
        var found = new HashSet<string>();
        Walk(root, found, []);
        return found;
    }

    private static void Walk(Type type, HashSet<string> found, HashSet<Type> seen)
    {
        // Les DTOs se référencent en arbre, mais rien n'interdit un cycle : sans garde,
        // le premier en ferait tourner le test indéfiniment.
        if (!seen.Add(type)) return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Les propriétés calculées ne voyagent pas : System.Text.Json les sérialise,
            // mais elles ne font que reformuler les champs déjà couverts ci-dessous.
            if (property.GetMethod is null || property.GetIndexParameters().Length > 0) continue;

            var propertyType = Unwrap(property.PropertyType);

            if (propertyType == typeof(string))
            {
                found.Add($"{type.Name}.{property.Name}");
                continue;
            }

            if (ElementOf(propertyType) is { } element) propertyType = Unwrap(element);

            // Une liste de chaînes porte du texte tout autant qu'une chaîne. La manquer
            // laisserait passer exactement le champ où l'on rangerait des propos —
            // « les derniers messages », au pluriel.
            if (propertyType == typeof(string))
            {
                found.Add($"{type.Name}.{property.Name}");
                continue;
            }

            // On ne descend que dans nos propres types : Guid et DateTime ne portent
            // aucun texte, et parcourir le framework n'apprendrait rien.
            if (propertyType.Namespace?.StartsWith("Cours.") == true)
                Walk(propertyType, found, seen);
        }
    }

    private static Type Unwrap(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static Type? ElementOf(Type type)
    {
        if (type.IsArray) return type.GetElementType();

        return type.IsGenericType && type.GetGenericArguments().Length == 1
            ? type.GetGenericArguments()[0]
            : null;
    }
}
