#!/usr/bin/env bash
# Tests de bout en bout de la phase 1 bis : partage des cours (Cours) et évaluations (Quiz).
#
# Prérequis : services User, Cours et Quiz démarrés en Development sur des bases NEUVES
# (données de démonstration du service User), et pour la correction assistée un LLM
# compatible OpenAI (ou un faux serveur) configuré dans Quiz (LLM:BaseUrl).
#   USER_API=http://localhost:5197 COURS_API=http://localhost:5188 QUIZ_API=http://localhost:5163 \
#   bash scripts/test-evaluations.sh
set -u
USER_API=${USER_API:-http://localhost:5197}
COURS_API=${COURS_API:-http://localhost:5188}
QUIZ_API=${QUIZ_API:-http://localhost:5163}
PASS=0; FAIL=0
ok()   { PASS=$((PASS+1)); echo "  ✓ $1"; }
ko()   { FAIL=$((FAIL+1)); echo "  ✗ $1  →  $2"; }
check(){ if [ "$2" == "$3" ]; then ok "$1"; else ko "$1" "attendu [$3], obtenu [$2]"; fi; }

# req METHOD URL TOKEN [BODY] → $BODY, $CODE
req() {
  local auth=(); [ -n "$3" ] && auth=(-H "Authorization: Bearer $3")
  local out; out=$(curl -s -w '\n%{http_code}' -X "$1" "$2" "${auth[@]}" -H 'Content-Type: application/json' ${4:+-d "$4"})
  BODY=$(echo "$out" | sed '$d'); CODE=$(echo "$out" | tail -n1)
}
login() { req POST "$USER_API/auth/login" "" "{\"email\":\"$1\",\"password\":\"Test1234!\"}"; echo "$BODY" | jq -r .token; }
register() { req POST "$USER_API/auth/register" "" "$1"; echo "$BODY" | jq -r .token; }
iso() { date -u -d "$1" +%Y-%m-%dT%H:%M:%SZ; }

echo "── Préparation (comptes de démonstration + nouveaux comptes)"
PROF=$(login prof@gpoe26.cm); ELEVE=$(login eleve@gpoe26.cm); PARENT=$(login parent@gpoe26.cm)
req GET "$USER_API/classrooms/mine" "$PROF"; CLASS=$(echo "$BODY" | jq -r '.[0].id'); INVITE=$(echo "$BODY" | jq -r '.[0].inviteCode')
BOB=$(register "{\"email\":\"bob@t.cm\",\"username\":\"bob\",\"password\":\"Test1234!\",\"inviteCode\":\"$INVITE\"}")
INTRUS=$(register '{"email":"intrus@t.cm","username":"intrus","password":"Test1234!"}')
AUTRE=$(register '{"email":"autre@t.cm","username":"autreprof","password":"Test1234!","role":"Teacher","initialClasses":["Première"]}')
req GET "$USER_API/classrooms/mine" "$AUTRE"; AUTRE_CLASS=$(echo "$BODY" | jq -r '.[0].id')
req GET "$USER_API/auth/me" "$ELEVE"; ELEVE_ID=$(echo "$BODY" | jq -r .id)
req GET "$USER_API/auth/me" "$BOB"; BOB_ID=$(echo "$BODY" | jq -r .id)
check "classe de démonstration trouvée" "$([ "$CLASS" != "null" ] && echo oui)" "oui"

echo "── Cours partagé avec la classe"
req POST "$COURS_API/cours" "$PROF" '{"title":"Suites géométriques","subject":"Mathématiques","description":"Chapitre 2","sections":[]}'
COURSE=$(echo "$BODY" | jq -r .id)
check "cours créé (brouillon)" "$(echo "$BODY" | jq -r .status)" "0"
req GET "$COURS_API/cours/$COURSE" "$ELEVE"
check "brouillon invisible pour l'élève" "$CODE" "404"
req PUT "$COURS_API/cours/$COURSE/sharing" "$PROF" "{\"publish\":true,\"classroomIds\":[\"$AUTRE_CLASS\"]}"
check "partage refusé avec la classe d'un autre enseignant" "$CODE" "400"
req PUT "$COURS_API/cours/$COURSE/sharing" "$PROF" "{\"publish\":true,\"classroomIds\":[\"$CLASS\"]}"
check "cours publié pour la classe" "$(echo "$BODY" | jq -r .status)" "1"
req GET "$COURS_API/cours/shared" "$ELEVE"
check "l'élève voit le cours partagé" "$(echo "$BODY" | jq -r '.[0].title')" "Suites géométriques"
req GET "$COURS_API/cours/$COURSE" "$ELEVE"
check "l'élève ouvre le cours" "$CODE" "200"
req GET "$COURS_API/cours/$COURSE" "$INTRUS"
check "un élève hors classe ne l'ouvre pas" "$CODE" "404"
req GET "$COURS_API/cours/shared" "$INTRUS"
check "…ni ne le voit dans sa liste" "$(echo "$BODY" | jq length)" "0"

echo "── Création et publication d'une épreuve (6 types de questions)"
DUE=$(iso "+1 hour")
EXAM=$(cat <<JSON
{"title":"Épreuve n°1 — Suites","kind":"GradedExam","subject":"Mathématiques","courseId":"$COURSE",
 "classroomIds":["$CLASS"],"dueAt":"$DUE","durationMinutes":60,"shuffle":true,"maxAttempts":1,
 "questions":[
  {"type":"SingleChoice","text":"u0 = 3, q = 2 : que vaut u4 ?","points":2,"options":["24","48","96","11"],"correctOptions":[1]},
  {"type":"MultipleChoice","text":"Quelles suites sont géométriques ?","points":2,"options":["2;4;8;16","1;3;5;7","5;15;45;135","1;1;2;3"],"correctOptions":[0,2]},
  {"type":"TrueFalse","text":"Une suite géométrique de raison -2 est croissante.","points":1,"trueFalseAnswer":false},
  {"type":"ShortAnswer","text":"Raison de 5 ; 15 ; 45 ; 135 ?","points":2,"acceptedAnswers":["3","trois"]},
  {"type":"Numeric","text":"Calculer 1 + 2 + … + 2^9.","points":3,"numericAnswer":1023,"tolerance":0},
  {"type":"OpenEnded","text":"Montrer que vn = un - 4 est géométrique.","points":6,"criteria":"Expression (2) ; factorisation (2) ; conclusion (2)"}
 ]}
JSON
)
req POST "$QUIZ_API/assessments" "$ELEVE" "$EXAM"
check "un élève ne crée pas d'épreuve" "$CODE" "403"
req POST "$QUIZ_API/assessments" "$PROF" '{"title":"Vide"}'
EMPTY=$(echo "$BODY" | jq -r .id)
req POST "$QUIZ_API/assessments/$EMPTY/publish" "$PROF"
check "publication refusée : incomplète" "$CODE" "400"
check "…avec la liste des erreurs" "$(echo "$BODY" | jq '.errors | length')" "2"
req POST "$QUIZ_API/assessments" "$PROF" "$EXAM"
check "épreuve créée en brouillon" "$(echo "$BODY" | jq -r .status)" "Draft"
check "barème total 16 points" "$(echo "$BODY" | jq -r .maxScore)" "16"
ID=$(echo "$BODY" | jq -r .id)
Q=($(echo "$BODY" | jq -r '.questions[].id'))
req GET "$QUIZ_API/assessments/mine" "$ELEVE"
check "brouillon invisible pour l'élève" "$(echo "$BODY" | jq length)" "0"
req POST "$QUIZ_API/assessments/$ID/publish" "$PROF"
check "épreuve publiée" "$(echo "$BODY" | jq -r .status)" "Published"
WRONG=$(echo "$EXAM" | jq -c --arg c "$AUTRE_CLASS" '.classroomIds=[$c] | .title="Mauvaise classe"')
req POST "$QUIZ_API/assessments" "$PROF" "$WRONG"; WID=$(echo "$BODY" | jq -r .id)
req POST "$QUIZ_API/assessments/$WID/publish" "$PROF"
check "publication refusée pour la classe d'un autre" "$CODE" "400"
req DELETE "$QUIZ_API/assessments/$WID" "$PROF"
check "brouillon sans copie supprimé" "$CODE" "204"

echo "── Accès élève"
req GET "$QUIZ_API/assessments/mine" "$ELEVE"
check "l'élève voit l'épreuve de sa classe" "$(echo "$BODY" | jq -r '.[0].title')" "Épreuve n°1 — Suites"
req GET "$QUIZ_API/assessments/mine" "$INTRUS"
check "l'intrus ne voit rien" "$(echo "$BODY" | jq length)" "0"
req POST "$QUIZ_API/assessments/$ID/attempts" "$INTRUS"
check "l'intrus ne peut pas composer" "$CODE" "404"
req GET "$QUIZ_API/assessments/$ID" "$AUTRE"
check "un autre enseignant ne la voit pas" "$CODE" "404"

echo "── Tentative de l'élève"
req POST "$QUIZ_API/assessments/$ID/attempts" "$ELEVE"
check "tentative démarrée" "$CODE" "201"
SUB=$(echo "$BODY" | jq -r .submissionId)
check "6 questions" "$(echo "$BODY" | jq '.questions | length')" "6"
check "aucune bonne réponse envoyée à l'élève" "$(echo "$BODY" | grep -cE 'correctOptions|acceptedAnswers|numericAnswer|trueFalseAnswer|criteria')" "0"
check "options avec leur index d'origine" "$(echo "$BODY" | jq '[.questions[] | select(.type=="SingleChoice") | .options[].index] | sort | join(",")' -r)" "0,1,2,3"
check "échéance = fin de la durée ou date limite" "$([ "$(echo "$BODY" | jq -r .deadline)" != "null" ] && echo oui)" "oui"
req POST "$QUIZ_API/assessments/$ID/attempts" "$ELEVE"
check "reprise de la même tentative" "$(echo "$BODY" | jq -r .submissionId)" "$SUB"
req PUT "$QUIZ_API/assessments/$ID" "$PROF" "$EXAM"
check "modification bloquée dès qu'un élève compose" "$CODE" "409"

ANSWERS=$(cat <<JSON
{"answers":[
 {"questionId":"${Q[0]}","selectedOptions":[1]},
 {"questionId":"${Q[1]}","selectedOptions":[0]},
 {"questionId":"${Q[2]}","trueFalse":false},
 {"questionId":"${Q[3]}","text":" Trois "},
 {"questionId":"${Q[4]}","text":"1 023"},
 {"questionId":"${Q[5]}","text":"v(n+1) = u(n+1) - 4 = 0,5 un - 2 = 0,5 (un - 4) = 0,5 vn : géométrique de raison 0,5."}
]}
JSON
)
req POST "$QUIZ_API/assessments/$ID/attempts/$SUB/submit" "$ELEVE" "$ANSWERS"
check "copie rendue, en attente de correction" "$(echo "$BODY" | jq -r .status)" "Submitted"
check "note cachée avant la date limite" "$(echo "$BODY" | jq -r .score)" "null"
check "correction cachée avant la date limite" "$(echo "$BODY" | jq -r '[.answers[].correction] | map(select(. != null)) | length')" "0"
req POST "$QUIZ_API/assessments/$ID/attempts/$SUB/submit" "$ELEVE" "$ANSWERS"
check "impossible de rendre deux fois" "$CODE" "409"
req POST "$QUIZ_API/assessments/$ID/attempts" "$ELEVE"
check "plus de tentative disponible" "$CODE" "409"

req POST "$QUIZ_API/assessments/$ID/attempts" "$BOB"; BSUB=$(echo "$BODY" | jq -r .submissionId)
req POST "$QUIZ_API/assessments/$ID/attempts/$BSUB/submit" "$BOB" "{\"answers\":[{\"questionId\":\"${Q[0]}\",\"selectedOptions\":[0]},{\"questionId\":\"${Q[4]}\",\"text\":\"1024\"}]}"
check "copie de bob : tout auto (réponse ouverte vide = 0)" "$(echo "$BODY" | jq -r .status)" "Graded"

echo "── Correction par l'enseignant"
req GET "$QUIZ_API/assessments/$ID/submissions" "$PROF"
check "2 copies" "$(echo "$BODY" | jq length)" "2"
check "noms des élèves" "$(echo "$BODY" | jq -r '[.[].studentName] | sort | join(",")')" "EleveTest,bob"
check "1 réponse à corriger pour EleveTest" "$(echo "$BODY" | jq -r '.[] | select(.studentName=="EleveTest") | .pendingManualAnswers')" "1"
req GET "$QUIZ_API/assessments/$ID/submissions" "$AUTRE"
check "un autre enseignant n'accède pas aux copies" "$CODE" "404"
req POST "$QUIZ_API/assessments/$ID/submissions/$SUB/answers/${Q[5]}/ai-suggestion" "$PROF"
check "proposition de l'IA" "$CODE" "200"
check "note IA arrondie au quart de point" "$(echo "$BODY" | jq -r .points)" "4.5"
req POST "$QUIZ_API/assessments/$ID/submissions/$SUB/answers/${Q[0]}/ai-suggestion" "$PROF"
check "IA réservée aux réponses ouvertes" "$CODE" "400"
req PUT "$QUIZ_API/assessments/$ID/submissions/$SUB/answers/${Q[5]}/grade" "$PROF" '{"points":7}'
check "note supérieure au barème refusée" "$CODE" "400"
req PUT "$QUIZ_API/assessments/$ID/submissions/$SUB/answers/${Q[5]}/grade" "$PROF" '{"points":5,"feedback":"Bien, conclusion à préciser (v0 = 6)."}'
check "copie corrigée" "$(echo "$BODY" | jq -r .status)" "Graded"
check "note 13/16" "$(echo "$BODY" | jq -r '.score + 0')" "13"
check "soit 16,25/20" "$(echo "$BODY" | jq -r .scoreScaled)" "16.25"
check "l'enseignant voit la proposition de l'IA" "$(echo "$BODY" | jq -r '.answers[] | select(.type=="OpenEnded") | .aiSuggestedPoints + 0')" "4.5"
req GET "$QUIZ_API/assessments/$ID/submissions/$SUB" "$ELEVE"
check "toujours caché pour l'élève avant clôture" "$(echo "$BODY" | jq -r '(.resultsReleased|tostring) + "/" + (.score|tostring)')" "false/null"
check "l'élève ne voit jamais la proposition de l'IA" "$(echo "$BODY" | jq -r '[.answers[].aiSuggestedPoints] | map(select(. != null)) | length')" "0"

echo "── Clôture et publication des résultats"
req POST "$QUIZ_API/assessments/$ID/close" "$PROF"
check "épreuve clôturée" "$(echo "$BODY" | jq -r .status)" "Closed"
req GET "$QUIZ_API/assessments/$ID/submissions/$SUB" "$ELEVE"
check "note visible après clôture" "$(echo "$BODY" | jq -r .scoreScaled)" "16.25"
check "correction et commentaire visibles" "$(echo "$BODY" | jq -r '.answers[] | select(.type=="OpenEnded") | .feedback')" "Bien, conclusion à préciser (v0 = 6)."
req GET "$QUIZ_API/assessments/$ID/submissions/$SUB" "$BOB"
check "bob ne voit pas la copie d'un autre" "$CODE" "404"

echo "── Suivi"
req GET "$QUIZ_API/progress/me" "$ELEVE"
check "moyenne de l'élève" "$(echo "$BODY" | jq -r .averageOn20)" "16.25"
check "maîtrise du cours 81,3 %" "$(echo "$BODY" | jq -r --arg c "$COURSE" '.mastery[] | select(.courseId==$c) | .percent')" "81.3"
req GET "$QUIZ_API/progress/classrooms/$CLASS" "$PROF"
check "tableau de classe : 2 élèves" "$(echo "$BODY" | jq '.students | length')" "2"
check "note de bob 0/20" "$(echo "$BODY" | jq -r '.students[] | select(.username=="bob") | .results[0].scoreScaled')" "0"
req GET "$QUIZ_API/progress/classrooms/$CLASS" "$AUTRE"
check "un autre enseignant n'a pas le tableau" "$CODE" "404"
req GET "$QUIZ_API/progress/students/$ELEVE_ID" "$PARENT"
check "le parent voit la note de son enfant" "$(echo "$BODY" | jq -r '.results[0].scoreScaled')" "16.25"
req GET "$QUIZ_API/progress/students/$BOB_ID" "$PARENT"
check "…mais pas celle d'un autre élève" "$CODE" "404"

echo "── Entraînement : correction immédiate"
req POST "$QUIZ_API/assessments" "$PROF" "{\"title\":\"Entraînement\",\"kind\":\"Practice\",\"classroomIds\":[\"$CLASS\"],\"maxAttempts\":3,\"questions\":[{\"type\":\"TrueFalse\",\"text\":\"0 < q < 1 et u0 > 0 : suite décroissante.\",\"points\":1,\"trueFalseAnswer\":true}]}"
PID=$(echo "$BODY" | jq -r .id); PQ=$(echo "$BODY" | jq -r '.questions[0].id')
req POST "$QUIZ_API/assessments/$PID/publish" "$PROF"
req POST "$QUIZ_API/assessments/$PID/attempts" "$ELEVE"; PSUB=$(echo "$BODY" | jq -r .submissionId)
req POST "$QUIZ_API/assessments/$PID/attempts/$PSUB/submit" "$ELEVE" "{\"answers\":[{\"questionId\":\"$PQ\",\"trueFalse\":true}]}"
check "note immédiate 20/20" "$(echo "$BODY" | jq -r .scoreScaled)" "20"
check "correction immédiate" "$(echo "$BODY" | jq -r '.answers[0].correction.trueFalseAnswer')" "true"
req DELETE "$QUIZ_API/assessments/$PID" "$PROF"
check "suppression refusée : des copies existent" "$CODE" "409"

echo
echo "Résultat : $PASS réussis, $FAIL échoués"
[ "$FAIL" -eq 0 ]
