-- Retour arrière de la reprise v5 et des appartenances datées (docs/PLAN-APPRENANT-AU-CENTRE.md, lot J2).
-- DERNIER RECOURS : d'abord essayer Visibility__Enabled=false (le service User revient à la vue « membres actuels »).
--
-- À lancer seulement pour redéployer un service User ANTÉRIEUR au lot J2, qui ne connaît pas LeftAt : sans ce
-- script, il prendrait les anciens élèves pour des membres actuels. Les périodes closes sont perdues :
-- exporter d'abord (première requête), et garder la sauvegarde pg_dumpall faite avant la reprise.
--
--   psql -U postgres -d userdb -f scripts/rollback-v5.sql
--
BEGIN;

-- 1. Export des périodes closes et des départs datés (à copier avant de continuer)
SELECT "Id", "ClassroomId", "StudentId", "JoinedAt", "LeftAt" FROM "ClassroomMembers" WHERE "LeftAt" IS NOT NULL;
SELECT "Id", "OrganizationId", "Matricule", "Status", "LeftAt" FROM "OrganizationStudents" WHERE "LeftAt" IS NOT NULL;

-- 2. Plus qu'une ligne par (classe, élève), comme avant J2 : les périodes closes disparaissent
DELETE FROM "ClassroomMembers" WHERE "LeftAt" IS NOT NULL;
DROP INDEX IF EXISTS "IX_ClassroomMembers_ClassroomId_StudentId";
ALTER TABLE "ClassroomMembers" DROP CONSTRAINT IF EXISTS "PK_ClassroomMembers";
ALTER TABLE "ClassroomMembers" ADD CONSTRAINT "PK_ClassroomMembers" PRIMARY KEY ("ClassroomId", "StudentId");

-- 3. Contrainte de la v5 retirée ; les colonnes ajoutées restent (ignorées par l'ancien code)
ALTER TABLE "OrganizationStudents" DROP CONSTRAINT IF EXISTS "CK_OrganizationStudents_LeftAt";
DELETE FROM "SchemaVersions" WHERE "Version" = 5;

COMMIT;
