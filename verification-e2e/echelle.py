import json, urllib.request, urllib.error, uuid
U, C = "http://localhost:5197", "http://localhost:5188"
RUN = uuid.uuid4().hex[:6]

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
        return e.code, e.read().decode()[:150]

def register(u, role):
    _, r = call("POST", f"{U}/auth/register", body={
        "email": f"{RUN}-{u}@t.fr", "username": f"{u}-{RUN}",
        "password": "MotDePasse123!", "role": role})
    return r["token"], r["profile"]["id"]

prof, profId = register("prof30", "Teacher")
_, cls = call("POST", f"{U}/auth/classes", prof,
              {"name": "Terminale D", "subject": "Physique", "schoolYear": "2025-2026"})
code = cls["code"]

print("Inscription de 30 élèves…")
ids = []
for i in range(30):
    tok, sid = register(f"e{i:02d}", "Student")
    st, _ = call("POST", f"{U}/auth/me/classes", tok, {"code": code})
    assert st == 200, st
    ids.append(sid)

st, roster = call("GET", f"{U}/auth/classes/mes-eleves", prof)
print(f"  effectif renvoyé : {len(roster)} élèves")

with open("/tmp/marqueur.txt", "w") as f: f.write(RUN)
print("MARQUEUR_POSE")

st, ov = call("POST", f"{C}/suivi/classe", prof, {"studentIds": ids, "jours": 7})
print(f"  POST /suivi/classe → {st}, {len(ov['students'])} lignes")
json.dump({"prof": prof, "ids": ids, "un": ids[0]}, open("/tmp/ctx30.json", "w"))
