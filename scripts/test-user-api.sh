#!/usr/bin/env bash
# Tests de bout en bout du service User (comptes, classes, parents, abonnements) — curl + jq.
#
# À lancer sur une base NEUVE, service démarré en Development (données de démonstration) :
#   ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5197 \
#   ConnectionStrings__UserDB="Host=...;Database=userdb_test;..." Jwt__Key=... Jwt__Issuer=GPOE2026 Jwt__Audience=GPOE2026Users \
#   dotnet run --project User/User.csproj --no-launch-profile
#   bash scripts/test-user-api.sh
set -u
API=${API:-http://localhost:5197}
PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); echo "  ✓ $1"; }
ko()   { FAIL=$((FAIL+1)); echo "  ✗ $1  →  $2"; }
check(){ if [ "$2" == "$3" ]; then ok "$1"; else ko "$1" "attendu [$3], obtenu [$2]"; fi; }

# req METHOD PATH TOKEN BODY → écrit le corps dans $BODY et le code HTTP dans $CODE
req() {
  local auth=(); [ -n "$3" ] && auth=(-H "Authorization: Bearer $3")
  local out; out=$(curl -s -w '\n%{http_code}' -X "$1" "$API$2" "${auth[@]}" -H 'Content-Type: application/json' ${4:+-d "$4"})
  BODY=$(echo "$out" | sed '$d'); CODE=$(echo "$out" | tail -n1)
}
login() { req POST /auth/login "" "{\"email\":\"$1\",\"password\":\"Test1234!\"}"; echo "$BODY" | jq -r .token; }
claim() { echo "$1" | cut -d. -f2 | tr '_-' '/+' | base64 -d 2>/dev/null | jq -r ".$2"; }

echo "── Comptes de démonstration"
ADMIN=$(login admin@gpoe26.cm);   check "admin a le rôle Admin" "$(claim "$ADMIN" role)" "Admin"
PROF=$(login prof@gpoe26.cm);     check "prof connecté" "$(claim "$PROF" role)" "Teacher"
PARENT=$(login parent@gpoe26.cm); check "parent connecté" "$(claim "$PARENT" role)" "Parent"

echo "── Validation de l'inscription"
req POST /auth/register "" '{"email":"court@t.cm","username":"court","password":"123"}'
check "mot de passe trop court refusé" "$CODE" "400"
req POST /auth/register "" '{"email":"adm@t.cm","username":"adm","password":"Test1234!","role":"Admin"}'
check "inscription Admin refusée" "$CODE" "400"
req POST /auth/register "" '{"email":"cle@t.cm","username":"clefausse","password":"Test1234!","subscriptionKey":"FAM-XXXX-XXXX"}'
check "clé inconnue refusée" "$CODE" "400"
req POST /auth/register "" '{"email":"cle@t.cm","username":"clefausse","password":"Test1234!"}'
check "…et aucun compte à moitié créé (même email réutilisable)" "$CODE" "201"

echo "── Élève : clé d'établissement + code de classe"
req POST /auth/register "" '{"email":"aicha@t.cm","username":"aicha","password":"Test1234!","subSystem":"Francophone","level":"Terminale","filiere":"C","examTarget":"Baccalauréat","focusSubjects":["Mathématiques","Physique-Chimie"],"dailyStudyMinutes":30,"subscriptionKey":"etb demo prof","inviteCode":"dem-tlc"}'
check "inscription élève" "$CODE" "201"
AICHA=$(echo "$BODY" | jq -r .token)
check "Premium via la licence" "$(echo "$BODY" | jq -r .profile.isPremium)" "true"
check "plan = OrganizationLicense" "$(echo "$BODY" | jq -r .profile.plan)" "OrganizationLicense"
check "claim JWT premium" "$(claim "$AICHA" premium)" "true"
check "matières à renforcer enregistrées" "$(echo "$BODY" | jq -r '.profile.focusSubjects|join(",")')" "Mathématiques,Physique-Chimie"
AICHA_LINK=$(echo "$BODY" | jq -r .profile.linkCode)
check "code de rattachement parent généré" "$([[ $AICHA_LINK =~ ^[A-Z2-9]{3}-[A-Z2-9]{4}$ ]] && echo oui)" "oui"
req GET /classrooms/mine "$AICHA"
check "classe rejointe à l'inscription" "$(echo "$BODY" | jq -r '.[0].name')" "Terminale C"
check "l'élève ne voit pas le code d'invitation" "$(echo "$BODY" | jq -r '.[0].inviteCode')" "null"

echo "── Enseignant (établissement) : classes initiales + essai"
req POST /auth/register "" '{"email":"ndongo@t.cm","username":"ndongo","password":"Test1234!","role":"Teacher","teachingContext":"School","organizationName":"Lycée de Bonabéri","teachingSubjects":["Mathématiques"],"initialClasses":["Terminale","Form 5"],"startTrial":true}'
check "inscription enseignant" "$CODE" "201"
NDONGO=$(echo "$BODY" | jq -r .token)
check "essai gratuit actif" "$(echo "$BODY" | jq -r .profile.isPremium)" "true"
check "spécialité déduite des matières" "$(echo "$BODY" | jq -r .profile.specialite)" "Mathématiques"
req GET /classrooms/mine "$NDONGO"
check "2 classes créées" "$(echo "$BODY" | jq length)" "2"
check "Form 5 = sous-système anglophone" "$(echo "$BODY" | jq -r '.[]|select(.name=="Form 5").subSystem')" "Anglophone"
check "matière reprise" "$(echo "$BODY" | jq -r '.[0].subject')" "Mathématiques"
CLASS_ID=$(echo "$BODY" | jq -r '.[]|select(.name=="Terminale").id')
INVITE=$(echo "$BODY" | jq -r '.[]|select(.name=="Terminale").inviteCode')
req POST /subscriptions/trial "$NDONGO"
check "second essai refusé" "$CODE" "409"

echo "── Enseignant (autre institution) : groupes"
req POST /auth/register "" '{"email":"data@t.cm","username":"formatrice","password":"Test1234!","role":"Teacher","teachingContext":"Institution","organizationName":"Startup Kmer Data","initialClasses":["Promotion Data 2026"]}'
FORM=$(echo "$BODY" | jq -r .token)
req GET /classrooms/mine "$FORM"
check "groupe (et non classe)" "$(echo "$BODY" | jq -r '.[0].kind')" "Group"
check "pas de niveau scolaire" "$(echo "$BODY" | jq -r '.[0].level')" "null"

echo "── Rejoindre une classe / suivi enseignant"
req POST /classrooms/join "$AICHA" "{\"code\":\"$INVITE\"}"
check "élève rejoint la classe de M. Ndongo" "$CODE" "200"
req POST /classrooms/join "$AICHA" "{\"code\":\"$INVITE\"}"
check "rejoindre deux fois est idempotent" "$(echo "$BODY" | jq -r .memberCount)" "1"
req GET "/classrooms/$CLASS_ID/members" "$NDONGO"
check "l'enseignant voit son élève" "$(echo "$BODY" | jq -r '.[0].username')" "aicha"
req GET "/classrooms/$CLASS_ID/members" "$PROF"
check "un autre enseignant ne voit pas la liste" "$CODE" "404"
req POST /classrooms/join "$AICHA" '{"code":"ZZZ-ZZZ"}'
check "code invalide" "$CODE" "404"

echo "── Contrôle des rôles"
req POST /classrooms "$AICHA" '{"name":"pirate"}'
check "élève ne peut pas créer de classe" "$CODE" "403"
req POST /classrooms/join "$PARENT" "{\"code\":\"$INVITE\"}"
check "parent ne peut pas rejoindre de classe" "$CODE" "403"
req POST /admin/organizations "$PROF" '{"name":"x"}'
check "enseignant n'accède pas à l'admin" "$CODE" "403"
req GET /classrooms/mine ""
check "sans jeton → 401" "$CODE" "401"

echo "── Parent : formule familiale + clé"
req POST /auth/register "" '{"email":"clarisse@t.cm","username":"clarisse","password":"Test1234!","role":"Parent"}'
CLARISSE=$(echo "$BODY" | jq -r .token)
req POST /subscriptions/checkout "$CLARISSE" '{"plan":"StudentPremium"}'
check "formule élève refusée au parent" "$CODE" "400"
req POST /subscriptions/checkout "$CLARISSE" '{"plan":"Family","period":"Yearly"}'
check "souscription en attente de paiement" "$(echo "$BODY" | jq -r .status)" "PendingPayment"
SUB=$(echo "$BODY" | jq -r .id)
req POST "/admin/subscriptions/$SUB/activate" "$ADMIN" '{}'
check "activation par l'admin" "$(echo "$BODY" | jq -r .status)" "Active"
FAMKEY=$(echo "$BODY" | jq -r '.keys[0].code')
check "clé familiale FAM-xxxx-xxxx" "$([[ $FAMKEY =~ ^FAM-[A-Z2-9]{4}-[A-Z2-9]{4}$ ]] && echo oui)" "oui"
check "3 places" "$(echo "$BODY" | jq -r '.keys[0].maxUses')" "3"
req POST "/admin/subscriptions/$SUB/activate" "$ADMIN" '{}'
check "double activation refusée" "$CODE" "409"
req POST /subscriptions/redeem "$CLARISSE" "{\"code\":\"$FAMKEY\"}"
check "le parent ne consomme pas sa propre clé" "$CODE" "400"

echo "── Rattachement par code (avec accord de l'élève)"
req POST /auth/register "" '{"email":"junior@t.cm","username":"junior","password":"Test1234!","level":"4e"}'
JUNIOR=$(echo "$BODY" | jq -r .token); JUNIOR_LINK=$(echo "$BODY" | jq -r .profile.linkCode)
req POST /parents/children/link "$CLARISSE" "{\"code\":\"$JUNIOR_LINK\"}"
check "demande en attente" "$(echo "$BODY" | jq -r .status)" "Pending"
check "niveau masqué tant que non accepté" "$(echo "$BODY" | jq -r .level)" "null"
req GET /me/parents "$JUNIOR"
LINK=$(echo "$BODY" | jq -r '.[0].linkId')
check "l'élève voit la demande de clarisse" "$(echo "$BODY" | jq -r '.[0].parentName')" "clarisse"
req POST "/me/parents/$LINK/approve" "$JUNIOR"
check "l'élève accepte" "$CODE" "204"
req POST "/me/parents/$LINK/approve" "$JUNIOR"
check "réponse déjà donnée" "$CODE" "409"
req GET /auth/me "$JUNIOR"
check "place familiale attribuée automatiquement" "$(echo "$BODY" | jq -r .plan)" "Family"

echo "── Le parent crée le compte d'un enfant"
req POST /parents/children "$CLARISSE" '{"email":"cadette@t.cm","username":"cadette","password":"Test1234!","subSystem":"Anglophone","level":"Form 2"}'
check "compte enfant créé" "$CODE" "201"
check "lien actif + Premium" "$(echo "$BODY" | jq -r '.status + "/" + (.isPremium|tostring)')" "Active/true"
req GET /parents/children "$CLARISSE"
check "le parent voit ses 2 enfants" "$(echo "$BODY" | jq length)" "2"

echo "── Clé familiale saisie par un élève + plafond de places"
req POST /subscriptions/redeem "$AICHA" "{\"code\":\"$FAMKEY\"}"
check "3e place utilisée par un élève" "$CODE" "200"
req POST /auth/register "" "{\"email\":\"quatre@t.cm\",\"username\":\"quatrieme\",\"password\":\"Test1234!\",\"subscriptionKey\":\"$FAMKEY\"}"
check "4e enfant refusé : clé pleine" "$CODE" "400"
req POST /subscriptions/redeem "$AICHA" "{\"code\":\"$FAMKEY\"}"
check "réactiver sa propre clé reste OK" "$CODE" "200"
req GET /subscriptions/me "$CLARISSE"
check "compteur 3/3" "$(echo "$BODY" | jq -r '.owned[0].keys[0] | "\(.usedCount)/\(.maxUses)"')" "3/3"

echo "── Licence d'institution"
req POST /admin/organizations "$ADMIN" '{"name":"Startup Kmer Data","type":"Institution","city":"Yaoundé"}'
ORG=$(echo "$BODY" | jq -r .id)
req POST "/admin/organizations/$ORG/licenses" "$ADMIN" '{"seats":5,"months":6}'
ORGKEY=$(echo "$BODY" | jq -r '.keys[0].code')
check "clé d'institution ORG-…" "${ORGKEY:0:4}" "ORG-"
req POST /subscriptions/redeem "$FORM" "{\"code\":\"$ORGKEY\"}"
check "la formatrice active la licence" "$(echo "$BODY" | jq -r .isPremium)" "true"
req GET /auth/me "$FORM"
check "rattachée à l'institution" "$(echo "$BODY" | jq -r .organizationId)" "$ORG"
req POST /subscriptions/redeem "$CLARISSE" "{\"code\":\"$ORGKEY\"}"
check "un parent ne peut pas utiliser une licence" "$CODE" "400"

echo
echo "Résultat : $PASS réussis, $FAIL échoués"
[ "$FAIL" -eq 0 ]
