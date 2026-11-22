# Données d'essai : cours de physique partagé, exercice d'entraînement avec réponse développée, élève Premium.
import json, urllib.request, sys
U,C,Q='http://localhost:5197','http://localhost:5188','http://localhost:5163'
def call(method,url,body=None,tok=None):
    req=urllib.request.Request(url,method=method,data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json'})
    if tok: req.add_header('Authorization','Bearer '+tok)
    try:
        with urllib.request.urlopen(req) as r: t=r.read(); return json.loads(t) if t else None
    except urllib.error.HTTPError as e: sys.exit(f"{method} {url} -> {e.code} {e.read()[:300]}")
login=lambda e: call('POST',U+'/auth/login',{'email':e,'password':'Test1234!'})['token']
prof=login('prof@gpoe26.cm'); admin=login('admin@gpoe26.cm'); eleve=login('eleve@gpoe26.cm')
cls=[c for c in call('GET',U+'/classrooms/mine',tok=prof) if c['name']=='Terminale C'][0]['id']
md=[
 (0,'Le mouvement rectiligne uniformément varié (MRUV)',1),
 (1,"Un mobile est en **MRUV** lorsque sa trajectoire est une droite et que son accélération $a$ est constante.",0),
 (0,'Équations horaires',1),
 (1,"Avec $x_0$ la position initiale et $v_0$ la vitesse initiale :\n\n$$x(t) = \\frac{1}{2} a t^2 + v_0 t + x_0$$\n\n$$v(t) = a t + v_0$$",0),
 (0,'Relation indépendante du temps',1),
 (1,"$$v^2 - v_0^2 = 2a(x - x_0)$$\n\nElle est très utile quand la durée n'est pas donnée.",0),
 (0,'Chute libre',1),
 (1,"Un corps lâché sans vitesse initiale tombe en MRUV avec $a = g \\approx 10\\ \\text{m/s}^2$ : $h = \\frac{1}{2} g t^2$.",0),
]
course=call('POST',C+'/cours',{'title':'Mouvement rectiligne uniformément varié','subject':'Physique','description':"Équations horaires, relation indépendante du temps et chute libre.",
  'sections':[{'type':t,'content':c,'order':i,'level':l} for i,(t,c,l) in enumerate(md)]},tok=prof)
call('PUT',C+f"/cours/{course['id']}/sharing",{'publish':True,'classroomIds':[cls]},tok=prof)
a=call('POST',Q+'/assessments',{'title':'Entraînement — MRUV','kind':'Practice','subject':'Physique','courseId':course['id'],'classroomIds':[cls],
  'maxAttempts':3,'aiAssistedGrading':True,'showCorrection':True,'questions':[
   {'type':'SingleChoice','text':"Dans un MRUV, l'accélération est…",'points':1,'options':['nulle','constante','proportionnelle au temps'],'correctOptions':[1]},
   {'type':'OpenEnded','text':"Une voiture part du repos avec une accélération a = 2 m/s². Calculer la distance parcourue en 5 s puis la vitesse atteinte. Détailler les calculs.",
    'points':4,'criteria':"x = ½·a·t² = ½·2·5² = 25 m (2 pts, dont 1 pour la formule) ; v = a·t = 10 m/s (2 pts). Unités exigées.",
    'explanation':"x = ½ × 2 × 25 = 25 m ; v = 2 × 5 = 10 m/s."}]},tok=prof)
call('POST',Q+f"/assessments/{a['id']}/publish",tok=prof)
sub=call('POST',U+'/subscriptions/checkout',{'plan':'StudentPremium','period':'Monthly'},tok=eleve)
call('POST',U+f"/admin/subscriptions/{sub['id']}/activate",{'months':1},tok=admin)
print(json.dumps({'course':course['id'],'assessment':a['id']}))
