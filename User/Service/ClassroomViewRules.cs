using User.Model;

namespace User.Service;

/// <summary>
/// Quelles classes renvoie GET /classrooms/mine (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J1).
/// Une personne peut enseigner dans une classe et apprendre dans une autre : la réponse suit l'appartenance
/// réelle, plus le rôle du compte. Les services demandent explicitement ce qu'ils veulent :
/// <c>?as=learner</c> (classes rejointes), <c>?as=teacher</c> (classes enseignées), sans paramètre (les deux).
/// </summary>
public enum ClassroomView
{
    All,
    Learner,
    Teacher
}

public static class ClassroomViewRules
{
    /// <summary>Valeurs acceptées pour <c>as</c>, dans le message d'erreur.</summary>
    public const string Expected = "learner, teacher ou all";

    /// <summary>Vide ou absent : toutes les classes. Sinon learner, teacher ou all (casse indifférente).</summary>
    public static bool TryParse(string? value, out ClassroomView view)
    {
        view = ClassroomView.All;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "all": view = ClassroomView.All; return true;
            case "learner": view = ClassroomView.Learner; return true;
            case "teacher": view = ClassroomView.Teacher; return true;
            default: return false;
        }
    }

    public static bool WantsTaught(ClassroomView view) => view is ClassroomView.All or ClassroomView.Teacher;
    public static bool WantsJoined(ClassroomView view) => view is ClassroomView.All or ClassroomView.Learner;

    /// <summary>
    /// Place affichée pour une classe à la fois enseignée et rejointe (vue « all ») : l'enseignement l'emporte,
    /// c'est le seul cas où la personne peut y publier. En vue « learner », elle y reste apprenante.
    /// </summary>
    public static ClassroomRole RoleOf(ClassroomView view, bool teaches) =>
        view == ClassroomView.Learner || !teaches ? ClassroomRole.Student : ClassroomRole.Teacher;
}
