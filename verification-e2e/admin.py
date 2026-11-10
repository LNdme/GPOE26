"""Qui peut modifier le contenu du site — vérifié sur la pile réelle.

Avant cette étape, GPOE26.ApiService n'avait AUCUNE authentification : dix-huit
endpoints d'écriture et un dépôt de fichiers, ouverts à tout ce qui pouvait
l'atteindre. La colonne « anonyme » de ce tableau répondait 200 partout.

C'est elle qui compte. Tant qu'elle n'est pas entièrement en 401, rien n'est fermé.

    python3 verification-e2e/admin.py
"""
import json, urllib.request, urllib.error, uuid, sys

U, A = "http://localhost:5197", "http://localhost:5200"
RUN = uuid.uuid4().hex[:6]
ok = fail = 0


def call(method, url, token=None, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    if body is not None:
        req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode()[:160]


def check(label, got, want):
    global ok, fail
    good = got == want
    ok, fail = ok + good, fail + (not good)
    print(f"  {'PASS ' if good else 'ÉCHEC'}  {label:<52} {got} (attendu {want})")


def register(nom, role="Student"):
    st, r = call("POST", f"{U}/auth/register", body={
        "email": f"{RUN}-{nom}@t.fr", "username": f"{nom}-{RUN}",
        "password": "MotDePasse123!", "role": role})
    if not isinstance(r, dict):
        sys.exit(f"inscription {nom} refusée : {st} {r}")
    return r["token"], r["profile"]["id"]


def promouvoir(admin_token, user_id, role):
    st, _ = call("PUT", f"{U}/auth/comptes/{user_id}/role", admin_token, {"role": role})
    assert st == 200, f"promotion vers {role} refusée : {st}"


# ── Le verrou de l'inscription ────────────────────────────────────────────────
print("\n═══ L'inscription ne décerne pas de pouvoir\n")
for role in ("Admin", "Editor"):
    st, _ = call("POST", f"{U}/auth/register", body={
        "email": f"{RUN}-pirate-{role}@t.fr", "username": f"pirate-{role}-{RUN}",
        "password": "MotDePasse123!", "role": role})
    check(f"s'inscrire comme {role}", st, 400)

# ── Les comptes ───────────────────────────────────────────────────────────────
# Le premier administrateur vient de l'amorçage (Admin__BootstrapEmail) ; ici on le
# fabrique en base, ce que le script documente plutôt que de le cacher.
eleve_tok, eleve_id = register("eleve")
admin_tok, admin_id = register("admin")
redac_tok, redac_id = register("redac")

import subprocess
subprocess.run(["su", "-", "postgres", "-c", "psql -q -d userdb"], text=True,
               input=f"UPDATE \"AppUsers\" SET \"Role\" = 'Admin' WHERE \"Id\" = '{admin_id}';",
               capture_output=True, check=True)

# Le jeton porte le rôle : il faut le réémettre après la promotion en base.
_, r = call("POST", f"{U}/auth/login", body={"email": f"{RUN}-admin@t.fr", "password": "MotDePasse123!"})
admin_tok = r["token"]
check("l'administrateur amorcé porte bien son rôle", r["profile"]["role"], "Admin")

promouvoir(admin_tok, redac_id, "Editor")
_, r = call("POST", f"{U}/auth/login", body={"email": f"{RUN}-redac@t.fr", "password": "MotDePasse123!"})
redac_tok = r["token"]
check("le rédacteur a été promu par l'administrateur", r["profile"]["role"], "Editor")

# ── Le tableau ────────────────────────────────────────────────────────────────
article  = lambda: {"title": f"Article {uuid.uuid4().hex[:5]}", "content": "Texte.", "category": "Vie scolaire"}
membre   = lambda: {"name": f"M. {uuid.uuid4().hex[:5]}", "role": "Censeur", "orderIndex": 9}

appelants = [("anonyme", None), ("élève", eleve_tok), ("rédacteur", redac_tok), ("administrateur", admin_tok)]

#                        anonyme  élève  rédacteur  admin
attendu = {
    "news":      [401, 403, 201, 201],
    "hierarchy": [401, 403, 403, 201],
}

print("\n═══ Écriture de contenu — /api/news (rédaction)\n")
for (nom, tok), want in zip(appelants, attendu["news"]):
    st, _ = call("POST", f"{A}/api/news", tok, article())
    check(f"{nom} publie une actualité", st, want)

print("\n═══ Gouvernance — /api/hierarchy (administration)\n")
for (nom, tok), want in zip(appelants, attendu["hierarchy"]):
    st, _ = call("POST", f"{A}/api/hierarchy", tok, membre())
    check(f"{nom} modifie l'équipe de direction", st, want)

print("\n═══ Dépôt de fichiers — /api/upload\n")
#
# Le plus dangereux des dix-neuf : il écrit sur le disque du serveur. Les extensions
# étaient déjà filtrées, mais un filtre d'extension n'a jamais remplacé une
# autorisation — n'importe qui pouvait remplir le disque, ou remplacer des images.

def televerser(token):
    """Un PNG minimal, en multipart/form-data, sans dépendance externe."""
    png = bytes.fromhex("89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489"
                        "0000000a49444154789c6300010000050001")
    front = b"--X\r\nContent-Disposition: form-data; name=\"file\"; filename=\"t.png\"\r\n" \
            b"Content-Type: image/png\r\n\r\n"
    corps = front + png + b"\r\n--X--\r\n"

    req = urllib.request.Request(f"{A}/api/upload", data=corps, method="POST")
    req.add_header("Content-Type", "multipart/form-data; boundary=X")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            return r.status
    except urllib.error.HTTPError as e:
        return e.code

for nom, tok, want in [("anonyme", None, 401), ("élève", eleve_tok, 403),
                       ("rédacteur", redac_tok, 200), ("administrateur", admin_tok, 200)]:
    check(f"{nom} dépose un fichier", televerser(tok), want)

print("\n═══ Les lectures restent publiques\n")
st, _ = call("GET", f"{A}/api/news")
check("un anonyme lit les actualités", st, 200)
st, _ = call("GET", f"{A}/api/hierarchy")
check("un anonyme lit l'équipe", st, 200)

print("\n═══ Les comptes — réservés à l'administration\n")
for nom, tok, want in [("anonyme", None, 401), ("élève", eleve_tok, 403),
                       ("rédacteur", redac_tok, 403), ("administrateur", admin_tok, 200)]:
    st, _ = call("GET", f"{U}/auth/comptes", tok)
    check(f"{nom} liste les comptes", st, want)

print("\n═══ Les deux garde-fous de la promotion\n")
st, _ = call("PUT", f"{U}/auth/comptes/{admin_id}/role", admin_tok, {"role": "Student"})
check("un administrateur se rétrograde lui-même", st, 400)
st, _ = call("PUT", f"{U}/auth/comptes/{eleve_id}/role", redac_tok, {"role": "Admin"})
check("un rédacteur promeut quelqu'un", st, 403)

print(f"\n═══ {ok} PASS / {fail} ÉCHEC")
sys.exit(1 if fail else 0)
