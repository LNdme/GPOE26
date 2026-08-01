# Harness B — le challenger

Seconde implémentation du contrat, en TypeScript. Elle existe pour être comparée à
`GPOE26.Harness` (.NET), et c'est le tableau produit par la suite de conformité qui
décidera laquelle déployer — pas une préférence.

## Pourquoi ce n'est pas QM

Le plan prévoyait d'adapter [QM](https://github.com/yc-software/qm) par son *deployment
directory*. La lecture du code a montré que cette route n'existe pas :

- L'interface `Harness` de QM n'est pas un contrat de service. C'est son abstraction interne de **pilote de modèle**, qui prend `Session`, `ScopeId`, `ToolContext`, `TapeRecord`, `SecurityScreenVerdict` — tous des types de son domaine. L'implémenter, c'est adopter son magasin de sessions, son modèle de portées, son système de bandes et de rejeu.
- Le *deployment directory* personnalise la configuration, les outils et les skills d'une organisation. **Il ne remplace pas le modèle de domaine.** Or celui de QM est « organisations, projets, salons, Slack, sandbox distant » ; le nôtre est « élève, cours, parcours, parent ». Ce n'est pas un changement de configuration.
- Son sandbox exécute côté serveur. Chez nous l'exécution est locale, en WASM, hors ligne — c'est là que les deux projets divergent le plus.

Ce qui est **emprunté à QM**, et lui revient :

| Idée | Où elle sert ici |
|---|---|
| Pilotes de modèle interchangeables | `src/drivers/` — la boucle ne dépend pas d'un fournisseur |
| Magasin de sessions à deux implémentations | `src/store/` — mémoire ou Postgres |
| Un pilote factice à côté des vrais | `src/drivers/mock-driver.ts` |

Le pilote factice mérite un mot : sans lui, la suite de conformité exigerait une clé
d'API pour vérifier des choses qui n'ont rien à voir avec un modèle — que les outils
exposés suivent la table, qu'un parent n'accède pas à l'enfant d'un autre, qu'un tour se
termine toujours. C'est ce qui rend ce harness vérifiable en intégration continue.

## Ce qui diffère vraiment du harness A

**La boucle est écrite ici.** Côté .NET, `FunctionInvokingChatClient` la fournit : on
déclare des fonctions et la bibliothèque enchaîne appels de modèle et exécutions d'outils.
Ici, `OpenRouterDriver` la tient à la main — ce qui donne un contrôle plus fin sur ce
qu'on montre à l'élève entre deux appels et sur le moment où l'on s'arrête, au prix du
code qu'il faut maintenir.

C'est la seule différence qui compte, et c'est celle que le match doit trancher.

## Lancer

```bash
npm install && npm run build && npm start
```

| Variable | Défaut | Rôle |
|---|---|---|
| `PORT` | `5300` | |
| `OpenRouter__ApiKey` | — | Absente, le pilote factice prend le relais |
| `ConnectionStrings__qmdb` | — | Absente, le magasin est en mémoire |
| `COURS_URL` | `http://localhost:5000` | |
| `Jwt__Key` | *(clé de développement)* | Doit être celle du service User |

## Le juge

```bash
HARNESS_TARGET=http://127.0.0.1:5300 dotnet test GPOE26.Harness.Conformance
```

Le même projet de tests que pour le harness .NET. Un test HTTP se moque de la langue du
service qu'il interroge — c'est ce qui rend la comparaison honnête.

## Ce que le match a déjà révélé

`/cours/{id}/rendu` **a quitté le contrat**. Le harness B ne pouvait l'honorer sans
embarquer un second moteur de rendu Markdown, ce qu'on cherchait précisément à éviter.
En cherchant pourquoi, il est apparu que rendre un cours n'est pas une affaire d'agent
mais de contenu : l'endpoint vit désormais dans le service Cours, qui possède le contenu.

C'est exactement ce à quoi sert une seconde implémentation — révéler ce que la première
avait mis là par commodité.
