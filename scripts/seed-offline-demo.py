# Données de l'essai hors connexion (phase 4) : un cours de maths partagé et un devoir « composable hors ligne »
# pour la classe de démonstration. Pile démarrée en Development (comptes de démonstration).
#   python3 scripts/seed-offline-demo.py > ids.json
import json, urllib.request, sys, datetime
U, C, Q = 'http://localhost:5197', 'http://localhost:5188', 'http://localhost:5163'

def call(method, url, body=None, tok=None):
    req = urllib.request.Request(url, method=method, data=None if body is None else json.dumps(body).encode(),
                                 headers={'Content-Type': 'application/json'})
    if tok: req.add_header('Authorization', 'Bearer ' + tok)
    try:
        with urllib.request.urlopen(req) as r:
            t = r.read(); return json.loads(t) if t else None
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {url} -> {e.code} {e.read()[:300]}")

login = lambda e: call('POST', U + '/auth/login', {'email': e, 'password': 'Test1234!'})['token']
prof = login('prof@gpoe26.cm')
cls = [c for c in call('GET', U + '/classrooms/mine', tok=prof) if c['inviteCode'] == 'DEM-TLC'][0]['id']
sections = [
    (0, 'Dérivée d\'une fonction', 1),
    (1, "La dérivée de $f$ en $a$ est la limite du taux d'accroissement : $$f'(a) = \\lim_{h \\to 0} \\frac{f(a+h) - f(a)}{h}$$", 0),
    (0, 'Dérivées usuelles', 1),
    (1, "- $(x^n)' = n x^{n-1}$\n- $(\\sqrt{x})' = \\frac{1}{2\\sqrt{x}}$\n- $(\\frac{1}{x})' = -\\frac{1}{x^2}$", 0),
    (0, 'Opérations', 1),
    (1, "$(u + v)' = u' + v'$, $(uv)' = u'v + uv'$ et $\\left(\\frac{u}{v}\\right)' = \\frac{u'v - uv'}{v^2}$.", 0),
]
course = call('POST', C + '/cours', {'title': 'Dérivation', 'subject': 'Mathématiques',
    'description': 'Nombre dérivé, dérivées usuelles et opérations.',
    'sections': [{'type': t, 'content': c, 'order': i, 'level': l} for i, (t, c, l) in enumerate(sections)]}, tok=prof)
call('PUT', C + f"/cours/{course['id']}/sharing", {'publish': True, 'classroomIds': [cls]}, tok=prof)
due = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=3)).isoformat()
a = call('POST', Q + '/assessments', {'title': 'Devoir — Dérivation', 'kind': 'Homework', 'subject': 'Mathématiques',
    'courseId': course['id'], 'classroomIds': [cls], 'dueAt': due, 'durationMinutes': 45, 'maxAttempts': 1,
    'allowOffline': True, 'aiAssistedGrading': True, 'questions': [
        {'type': 'SingleChoice', 'text': "La dérivée de x³ est…", 'points': 1, 'options': ['3x²', 'x²', '3x'], 'correctOptions': [0]},
        {'type': 'Numeric', 'text': "Soit f(x) = x² + 3x. Calculer f'(2).", 'points': 2, 'numericAnswer': 7, 'tolerance': 0},
        {'type': 'OpenEnded', 'text': "Dériver g(x) = (2x + 1)/(x − 3) en détaillant les étapes.", 'points': 3,
         'criteria': "Formule (u/v)' citée (1 pt) ; u' = 2, v' = 1 (1 pt) ; g'(x) = −7/(x − 3)² (1 pt)."}]}, tok=prof)
call('POST', Q + f"/assessments/{a['id']}/publish", tok=prof)
print(json.dumps({'course': course['id'], 'assessment': a['id']}))
