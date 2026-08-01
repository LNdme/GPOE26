import json, urllib.request, urllib.error, uuid, subprocess
U, C = "http://localhost:5197", "http://localhost:5188"
RUN = uuid.uuid4().hex[:6]

def call(method, url, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if body is not None: req.add_header("Content-Type", "application/json")
    if token: req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode(); return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e: return e.code, e.read().decode()[:150]

def register(u, role):
    _, r = call("POST", f"{U}/auth/register", body={
        "email": f"{RUN}-{u}@t.fr", "username": f"{u}-{RUN}",
        "password": "MotDePasse123!", "role": role})
    return r["token"], r["profile"]["id"]

prof, _ = register("profF", "Teacher")
_, cls = call("POST", f"{U}/auth/classes", prof,
              {"name": "1ère D", "subject": "Mathématiques", "schoolYear": "2025-2026"})
code = cls["code"]

eleves = []
for i in range(5):
    tok, sid = register(f"f{i}", "Student")
    call("POST", f"{U}/auth/me/classes", tok, {"code": code})
    eleves.append(sid)
E1, E2, E3, E4, E5 = eleves

COEF, AFFINE, THALES = "Le coefficient directeur", "La fonction affine", "Le théorème de Thalès"

# (élève, notion, nombre d'étapes en échec sur CETTE notion)
#
#   COEF   : 4 élèves distincts, dont E1 qui échoue TROIS fois  → doit compter 4, pas 6
#   AFFINE : 2 élèves distincts                                  → doit compter 2
#   THALES : 1 seul élève, qui échoue trois fois                 → ne doit PAS remonter
plan = [(E1, COEF, 3), (E2, COEF, 1), (E3, COEF, 1), (E4, COEF, 1),
        (E1, AFFINE, 1), (E2, AFFINE, 1),
        (E5, THALES, 3)]

sql = []
cours = {}
for eleve, notion, n in plan:
    if eleve not in cours:
        cid = str(uuid.uuid4()); cours[eleve] = cid
        sql.append(f"""INSERT INTO "Courses" ("Id","Title","Subject","ContentType","OwnerId",
            "CreatedAt","UpdatedAt","FormatStatus","JourneyMode")
            VALUES ('{cid}','Mathématiques — Chapitre 3','Mathématiques','markdown','{eleve}',
            now(),now(),'Done','PerSection');""")
    for k in range(n):
        sql.append(f"""INSERT INTO "CourseSteps" ("Id","CourseId","Kind","Order","Title","Status",
            "Attempts","WeakHeadings") VALUES ('{uuid.uuid4()}','{cours[eleve]}','MiniTest',
            {len(sql)},'Test — {notion}','Failed',1,'{notion}');""")

# psql tourne sous l'utilisateur postgres, qui n'a pas accès au scratchpad :
# on lui passe le script sur l'entrée standard plutôt que par un chemin de fichier.
r = subprocess.run(["su", "-", "postgres", "-c", "psql -q -d coursdb"],
                   input="\n".join(sql), text=True, capture_output=True)
if r.returncode: raise SystemExit("psql : " + r.stderr[:400])
print(f"Semé : {len(sql)} lignes ({len(cours)} cours, {len(sql)-len(cours)} étapes en échec)")

st, ov = call("POST", f"{C}/suivi/classe", prof, {"studentIds": eleves, "jours": 7})
print(f"\n═══ 8c. NOTIONS FRAGILES   (POST /suivi/classe → {st})\n")
for w in ov["weakSpots"]:
    print(f"   {w['studentCount']:>2} élève(s)   {w['heading']}")

got = {w["heading"]: w["studentCount"] for w in ov["weakSpots"]}
ok = fail = 0
def check(label, g, w):
    global ok, fail
    good = g == w; ok, fail = ok + good, fail + (not good)
    print(f"  {'PASS' if good else 'ÉCHEC'}  {label}   (obtenu {g}, attendu {w})")

print()
check("E1 rate 3× la même notion → compté UNE fois", got.get(COEF), 4)
check("une notion ratée par un seul élève ne remonte pas", THALES in got, False)
check("la notion la plus ratée arrive en tête", ov["weakSpots"][0]["heading"], COEF)
check("l'ordre est décroissant", [w["studentCount"] for w in ov["weakSpots"]],
      sorted((w["studentCount"] for w in ov["weakSpots"]), reverse=True))
check("la 2e notion est bien comptée", got.get(AFFINE), 2)
print(f"\n═══ {ok} PASS / {fail} ÉCHEC")
