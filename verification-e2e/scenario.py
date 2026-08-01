import json, urllib.request, urllib.error, sys, uuid

RUN = uuid.uuid4().hex[:6]   # emails neufs à chaque exécution

U, C = "http://localhost:5197", "http://localhost:5188"
ok = fail = 0

def call(method, url, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if body is not None: req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:200]

def check(label, got, want):
    global ok, fail
    good = got == want
    print(f"  {'PASS' if good else 'ÉCHEC'}  {label}   (obtenu {got}, attendu {want})")
    ok, fail = ok + good, fail + (not good)
    return good

def register(email, user, role):
    email, user = f"{RUN}-{email}", f"{user}-{RUN}"
    st, r = call("POST", f"{U}/auth/register", body={
        "email": email, "username": user, "password": "MotDePasse123!", "role": role})
    if not isinstance(r, dict): sys.exit(f"inscription {email} refusée : {st} {r}")
    return r["token"], r["profile"]["id"]

prof, profId = register("p2@t.fr", "prof2", "Teacher")
a,    aId    = register("a2@t.fr", "eleveA2", "Student")
b,    bId    = register("b2@t.fr", "eleveB2", "Student")   # témoin : ne rejoint jamais
c,    cId    = register("c2@t.fr", "eleveC2", "Student")
d,    dId    = register("d2@t.fr", "eleveD2", "Student")

print("\n═══ 2. Créer la classe")
st, cls = call("POST", f"{U}/auth/classes", prof,
               {"name": "Terminale C", "subject": "Mathématiques",
                "level": "Terminale", "schoolYear": "2025-2026"})
check("création", st, 201)
classId, code1 = cls["id"], cls["code"]
print(f"        code = {code1}  ({len(code1)} caractères)")

print("\n═══ 3. A rejoint par le code")
check("adhésion de A", call("POST", f"{U}/auth/me/classes", a, {"code": code1})[0], 200)
st, members = call("GET", f"{U}/auth/classes/{classId}/eleves", prof)
check("A dans l'effectif", [m["id"] for m in members], [aId])
st, roster = call("GET", f"{U}/auth/classes/mes-eleves", prof)
check("A dans mes-eleves", sorted(roster), sorted([aId]))

print("\n═══ 4. Régénérer le code")
st, cls2 = call("POST", f"{U}/auth/classes/{classId}/code", prof)
code2 = cls2["code"]
check("nouveau code émis", code2 != code1, True)
check("ANCIEN code refusé (C)", call("POST", f"{U}/auth/me/classes", c, {"code": code1})[0], 404)
check("nouveau code accepté (C)", call("POST", f"{U}/auth/me/classes", c, {"code": code2})[0], 200)

print("\n═══ 5. Fermer l'entrée")
check("fermeture", call("DELETE", f"{U}/auth/classes/{classId}/code", prof)[0], 204)
check("code refusé après fermeture (D) → 409 Conflict", call("POST", f"{U}/auth/me/classes", d, {"code": code2})[0], 409)
st, members = call("GET", f"{U}/auth/classes/{classId}/eleves", prof)
check("A et C restent inscrits", sorted(m["id"] for m in members), sorted([aId, cId]))

print("\n═══ 6. Étanchéité par élève (service Cours)")
check("élève de la classe (A) → 200", call("GET", f"{C}/suivi/{aId}/resume", prof)[0], 200)
check("élève HORS classe (B) → 403", call("GET", f"{C}/suivi/{bId}/resume", prof)[0], 403)
check("un élève ne voit pas un autre → 403", call("GET", f"{C}/suivi/{bId}/resume", a)[0], 403)
check("un élève se voit lui-même → 200", call("GET", f"{C}/suivi/{aId}/resume", a)[0], 200)

print("\n═══ 7. Liste falsifiée")
st, ov = call("POST", f"{C}/suivi/classe", prof, {"studentIds": [aId, bId, cId], "jours": 7})
check("appel accepté", st, 200)
check("seuls A et C ressortent", sorted(r["studentId"] for r in ov["students"]), sorted([aId, cId]))
check("B filtré", bId in [r["studentId"] for r in ov["students"]], False)

print("\n═══ Bilan partiel :", ok, "PASS /", fail, "ÉCHEC")
json.dump({"prof": prof, "profId": profId, "a": a, "aId": aId, "bId": bId,
           "cId": cId, "classId": classId}, open("/tmp/ctx.json", "w"))
sys.exit(1 if fail else 0)
