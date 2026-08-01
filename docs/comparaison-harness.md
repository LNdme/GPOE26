# Le match des deux harness

Deux implémentations du même contrat s'affrontent, et le déploiement se décide sur des
chiffres. Ce document tient le score.

| | **Harness A** | **Harness B** |
|---|---|---|
| Langage | C# / .NET 10 | TypeScript / Node |
| La boucle à outils | `FunctionInvokingChatClient` (fournie) | écrite à la main |
| Sessions | EF Core + Postgres | mémoire ou Postgres |
| Sans clé d'API | ne démarre pas | pilote factice |
| Réutilise l'existant | `OpenRouterClient`, `AgentKind`, `StudyIdentity` | rien — indépendance voulue |

## Étage conformité — déterministe, bloquant

Mesuré le 1er août 2026, pilote factice des deux côtés.

| | Harness A | Harness B |
|---|--:|--:|
| Tests passés | **32 / 32** | **24 / 24** |
| Outils conformes à la table | ✓ | ✓ |
| Droits parent-enfant | ✓ | ✓ |
| Test de fuite | ✓ | ✓ |
| Flux toujours terminé | ✓ | ✓ |
| Session reprise | ✓ | ✓ |

*Harness A subit huit tests de plus : ceux du juge, qui sabotent le stub de référence pour
vérifier que la suite sait tomber. Ils ne s'appliquent pas à une cible distante — on ne
saborde pas un service qu'on interroge par le réseau.*

## Étage comportement — mesuré, jamais bloquant

| | Harness A | Harness B |
|---|--:|--:|
| Premier token, médiane | 1 ms | 4 ms |
| Outils attendus appelés | 44 % | 44 % |
| Scénarios terminés | 10 / 10 | 10 / 10 |

⚠️ **Ces chiffres ne départagent rien, et il faut le dire.** Les deux tournent sur un
pilote factice qui joue toujours la même partition : les 44 % mesurent la partition, pas
le discernement d'un modèle. L'écart de latence — 1 ms contre 4 ms — sépare un appel en
mémoire d'un aller-retour HTTP local ; il disparaîtra derrière les centaines de
millisecondes d'un vrai appel de modèle.

**Ce que cette colonne prouve aujourd'hui, c'est que la mesure fonctionne.** Elle attend
une clé OpenRouter pour dire quelque chose.

## Ce qu'il reste à mesurer pour trancher

1. **Avec un vrai modèle, sur les dix scénarios.** C'est là que la boucle écrite à la main se compare à celle de la bibliothèque : sur le nombre d'allers-retours, les tokens consommés et la justesse des outils choisis.
2. **Sous charge**, avec plusieurs élèves simultanés.
3. **En panne** : que fait chacun quand OpenRouter renvoie un 429, quand Cours ne répond plus, quand la base tombe ?

## Ce que le match a déjà changé

Le harness B n'a pas pu honorer `/cours/{id}/rendu` sans embarquer un second moteur de
rendu Markdown. Plutôt que de le dupliquer, on a cherché pourquoi — et l'endpoint est
parti dans le service Cours, à qui appartient le contenu.

C'est le premier bénéfice concret d'avoir écrit deux implémentations, et il est arrivé
avant même la comparaison : **une seconde implémentation révèle ce que la première avait
mis là par commodité.**
