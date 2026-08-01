# Vérification end-to-end du suivi enseignant

Ce dossier existe parce que le projet a passé trois phases sans que rien ne tourne
jamais. Les tests d'unité *simulent* l'autorisation enseignant ; ils ne prouvent pas que
la pile assemblée se comporte comme eux — et c'est précisément là que vivait le défaut de
l'AppHost, qui privait Cours de toute référence vers User et refusait donc silencieusement
chaque enseignant.

Ce que ces scripts vérifient ne peut pas l'être autrement : ils démarrent les vrais
services, contre une vraie base, et regardent ce qui sort.

## Pourquoi sans Aspire

L'AppHost démarre PostgreSQL dans un conteneur. Là où il n'y a pas de runtime de
conteneurs, la pile se monte à la main — PostgreSQL installé, les deux services lancés
avec les variables qu'Aspire aurait injectées. **Aucune modification du code n'est
nécessaire**, uniquement des variables d'environnement.

```bash
apt-get install -y postgresql-16 postgresql-16-pgvector
pg_ctlcluster 16 main start
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD 'postgres';\" \
                       -c 'CREATE DATABASE userdb;' -c 'CREATE DATABASE coursdb;'"
```

Les migrations s'appliquent seules au démarrage de chaque service.

```bash
source verification-e2e/env.sh

# User — l'émetteur des jetons et le détenteur des classes
ConnectionStrings__userdb="$PG;Database=userdb" \
ASPNETCORE_URLS=http://localhost:5197 \
  dotnet run --project ./User --no-launch-profile &

# Cours — le suivi. La dernière ligne remplace le .WithReference(userService)
# de l'AppHost : sans elle, StudentDirectory n'atteint pas User et TOUT enseignant
# reçoit 403, sans message d'erreur.
ConnectionStrings__coursdb="$PG;Database=coursdb" \
ASPNETCORE_URLS=http://localhost:5188 \
Services__user__http__0=http://localhost:5197 \
  dotnet run --project ./Cours --no-launch-profile &
```

⚠️ **Le schéma dans `Services__user__http__0` n'est pas facultatif.** L'annuaire vise
`https+http://user` ; une valeur sans schéma (`localhost:5197`) fait tenter une poignée de
main TLS contre un écouteur en clair, et l'échec se présente comme un refus
d'autorisation. C'est l'erreur la plus coûteuse à diagnostiquer de tout ce montage.

## Les scripts

| Script | Ce qu'il vérifie |
|---|---|
| `scenario.py` | Le parcours complet : classe, code, adhésion, régénération, fermeture, étanchéité, liste falsifiée |
| `echelle.py` | 30 élèves ne produisent **qu'un seul** appel à User — à recouper avec les traces |
| `fragiles.py` | Les trois décisions de l'agrégation des notions fragiles |
| `fuite.py` | Aucun contenu d'échange dans le JSON de `/suivi/classe` |

Chacun s'exécute seul (`python3 verification-e2e/scenario.py`), sans dépendance autre que
la bibliothèque standard, et inscrit des comptes neufs à chaque exécution — on peut donc
les rejouer autant de fois que nécessaire sur la même base.

## Les deux points qui ne se vérifient pas d'un script

**L'échelle** se recoupe dans les traces de User. La requête d'effectif est reconnaissable :

```bash
grep -a -c 'SELECT DISTINCT c."StudentId"' user.log
```

Quatre consultations d'une classe de trente doivent donner **une** ligne, pas quatre ni
trente. Le reste est servi par le cache de cinq minutes.

**L'annuaire muet** demande d'arrêter User *et* de redémarrer Cours — le cache est en
mémoire, et sans redémarrage on mesurerait le cache plutôt que la panne. Un enseignant
demandant alors un élève de sa propre classe doit recevoir **403**. Une autorisation qui
s'ouvre quand elle ne peut pas vérifier n'est pas une autorisation.

## Ce que ces scripts ne couvrent pas

Tout ce qui appelle le modèle : le répétiteur, le bilan rédigé, et la suite de conformité
contre les deux harness. Ils demandent une clé OpenRouter
(`dotnet user-secrets set Parameters:openrouter-api-key … --project GPOE26.AppHost`).
