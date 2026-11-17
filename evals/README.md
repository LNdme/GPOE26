# Évaluations des agents (lot A0)

Ces évaluations mesurent la qualité des agents (tuteur, assistant de l'enseignant, agent du site) **tels qu'ils tournent**. Elles passent par les mêmes routes HTTP que l'interface. Elles servent de porte : une route ne bascule sur le nouveau runtime (plan `docs/PLAN-AGENT-NOYAU.md`, lot A7) que si elle fait au moins aussi bien que la référence.

Elles ne font pas partie de `dotnet test`, parce que chaque cas coûte des appels au modèle. Les règles de notation, elles, sont testées dans `GPOE26.Logic.Tests/EvalRulesTests.cs`.

## Contenu

| Dossier | Rôle |
|---|---|
| `cases/*.json` | les cas (60 au départ : tuteur 36, rédaction 12, agent du site 12) |
| `fixtures/courses/*.md` | cours d'essai, créés dans le compte élève à la première passe, puis recréés s'ils changent |
| `fixtures/images/` | photo de copie avec une erreur volontaire (le carré oublié) |
| `baseline.json` | la référence : le résultat contre lequel chaque passe est comparée |
| `results/` | les passes (JSON + résumé Markdown), non versionnées |

## Lancer une passe

Les services doivent tourner (Aspire). Il faut aussi des comptes d'essai. Utilisez de préférence des comptes dédiés : les cours d'essai apparaissent dans « Mes cours » de l'élève, et les questions comptent dans ses quotas d'IA (150 par jour par défaut).

```bash
export Evals__StudentEmail=… Evals__StudentPassword=…     # élève avec le tuteur (Premium)
export Evals__TeacherEmail=… Evals__TeacherPassword=…     # enseignant avec l'assistant (formule Enseignant)
export Evals__EditorEmail=… Evals__EditorPassword=…       # administrateur ou éditeur du site (sinon : l'enseignant)
dotnet run --project GPOE26.Evals -- run
```

Si l'AppHost tourne dans Visual Studio, ses binaires sont verrouillés. Compilez alors ailleurs :

```bash
dotnet build GPOE26.Evals --artifacts-path <dossier>
dotnet <dossier>/bin/GPOE26.Evals/debug/GPOE26.Evals.dll run
```

Options de `run` :
- `--agent tutor|compose|site` : un seul agent ;
- `--filter texte` : cas dont l'identifiant contient ce texte, ou qui portent cette étiquette (`injection`, `long`…) ;
- `--repeat N` : chaque cas N fois ; il en faut 3 pour décider d'une bascule, car les réponses varient ;
- `--judge` : ajoute la note du modèle juge (voir plus bas) ;
- `--label nom` : nom de la passe.

Autres commandes :
- `check` : vérifie les cas sans rien appeler ;
- `compare [résultat.json] [référence.json] [--margin 0.05]` : compare à la référence ;
- `baseline [résultat.json]` : fait d'un résultat la nouvelle référence. Par défaut, c'est la dernière passe.

## Lire un résultat

Chaque essai a un statut :
- **réussi** : toutes les règles du cas sont respectées ;
- **échec** : au moins une règle ne l'est pas ;
- **non joué** : refus attendu (droit, formule, quota, compte non configuré). Il n'est pas noté ;
- **panne** : modèle ou service indisponible. Il n'est pas noté. Au-delà de 10 % d'essais non joués, la comparaison est déclarée incomplète.

Le taux de réussite est donné avec son **intervalle de confiance à 95 %** (méthode de Wilson). La comparaison calcule l'écart avec la référence et son intervalle (méthode de Newcombe). Elle conclut de trois façons :
- **régression** : la baisse est certaine. La commande renvoie le code 1 et la bascule est refusée ;
- **au moins aussi bon** : une baisse de plus de 5 points est exclue. La bascule est permise ;
- **indéterminé** : il faut plus de répétitions ou plus de cas. Avec 60 essais, seule une grosse différence se voit : c'est pourquoi le plan vise plus de 200 cas avant la première bascule.

Limite connue : les essais d'un même cas ne sont pas indépendants les uns des autres. L'intervalle est donc un peu optimiste. Lisez aussi la liste « cas qui réussissaient toujours et échouent maintenant ».

## Le juge

La décision se prend sur les **règles**. Le juge complète : il note de 1 à 5 le critère (`rubric`) de chaque cas. Il ne remplace pas les règles. Le modèle juge est **fixe** : un seul modèle, sans repli, toujours le même d'une passe à l'autre. Sinon, les notes ne sont pas comparables.

```bash
export Evals__JudgeModel=<modèle payant précis, par exemple un modèle Claude ou GPT via OpenRouter>
dotnet run --project GPOE26.Evals -- run --judge
```

La clé est lue dans les secrets de l'AppHost (`Parameters:llm-apikey`), ou dans `LLM__ApiKey` ou `Evals__JudgeApiKey`. Si le juge ne répond pas, l'essai est marqué « non noté par le juge », jamais « réussi ».

## Écrire un cas

```jsonc
{
  "id": "tuteur-mruv-calcul-25m",          // unique, stable : la comparaison se fait par identifiant
  "agent": "tutor",                         // tutor | compose | site
  "course": "mruv",                         // tuteur : fixtures/courses/mruv.md
  "turns": ["question posée avant"],        // facultatif
  "message": "Une voiture part du repos…",
  "image": "copie-mruv-erreur.png",         // facultatif
  "checks": [ { "kind": "contains", "values": ["25"] } ],
  "rubric": "Trouve 25 m avec x = ½·a·t²."  // pour le juge
}
```

Les règles disponibles sont décrites dans `GPOE26.Evals/EvalCase.cs` (`Check`) : `contains`, `not_contains`, `regex`, `not_regex`, `min_length`, `max_length`, `language`, `latex`, `asks_question`, `action_count`, `action_types`, `action_field`, `destructive_flagged`.

Une règle doit être **vraie pour toute bonne réponse**. Par exemple, accepter « pas dans ton cours », « n'est pas dans », « ne figure pas »… plutôt qu'une seule formulation. Un cas qui échoue pour une mauvaise raison fausse la porte de bascule. Après avoir modifié un cas, lancez `check`, puis les tests.

## À venir

- Plus de 200 cas, et des profils d'apprenants de plusieurs pays (lot B2).
- Une mesure d'apprentissage : progrès au quiz sur les notions fragiles, avant et après. Elle demande des données d'usage réelles et viendra avec le registre d'activité.
