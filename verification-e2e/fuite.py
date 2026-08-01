"""Aucun contenu d'échange dans la réponse de /suivi/classe — sur le JSON réel.

Le pendant vivant de SuiviLeakTests, qui ne vérifie que la forme des types. Ici on
regarde ce qui sort réellement du service : c'est la seule preuve qui vaille, parce
qu'une fuite arrive par un champ auquel personne n'avait pensé.
"""
import json, urllib.request, uuid, sys

U, C = "http://localhost:5197", "http://localhost:5188"
RUN = uuid.uuid4().hex[:6]

def call(method, url, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if body is not None: req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", "Bearer " + token)
    with urllib.request.urlopen(req) as r:
        return r.read().decode()

def register(u, role):
    r = json.loads(call("POST", f"{U}/auth/register", body={
        "email": f"{RUN}-{u}@t.fr", "username": f"{u}-{RUN}",
        "password": "MotDePasse123!", "role": role}))
    return r["token"], r["profile"]["id"]

prof, _ = register("profL", "Teacher")
cls = json.loads(call("POST", f"{U}/auth/classes", prof,
                      {"name": "Test", "subject": "Maths", "schoolYear": "2025-2026"}))
eleves = []
for i in range(3):
    tok, sid = register(f"l{i}", "Student")
    call("POST", f"{U}/auth/me/classes", tok, {"code": cls["code"]})
    eleves.append(sid)

brut = call("POST", f"{C}/suivi/classe", prof, {"studentIds": eleves, "jours": 7})
print(f"Réponse de /suivi/classe : {len(brut)} octets\n")

# Des marqueurs d'échange, pas un champ nommé : on surveille ce qu'on n'attend pas.
marqueurs = ['"role"', '"content"', '"message"', '"messages"', "Élève :", "Eleve :",
             "assistant", "répétiteur", '"answer"', '"studentAnswer"', '"text"']
fail = 0
for m in marqueurs:
    trouve = m.lower() in brut.lower()
    fail += trouve
    print(f"  {'ÉCHEC' if trouve else 'PASS '}  absence de {m!r}")

print(f"\n  Champs de la réponse : {sorted(json.loads(brut).keys())}")
print(f"\n═══ {'FUITE DÉTECTÉE' if fail else 'aucune fuite'}")
sys.exit(1 if fail else 0)
