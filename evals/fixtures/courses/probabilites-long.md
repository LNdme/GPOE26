<!-- matière: Mathématiques -->
# Probabilités : du vocabulaire aux variables aléatoires

## Introduction

Les probabilités étudient les phénomènes dont on ne peut pas prévoir le résultat avec certitude, mais dont on peut décrire les chances. Lancer un dé, tirer une carte, observer le sexe d'un nouveau-né ou la durée de vie d'une ampoule sont des exemples de tels phénomènes. Le but de ce chapitre est de donner un vocabulaire précis, quelques règles de calcul sûres, et des méthodes pour organiser les situations, comme les arbres et les tableaux.

Ce cours est long : il va des définitions de base jusqu'aux variables aléatoires. Chaque partie se termine par une remarque ou un exemple. Les exemples numériques sont choisis pour que les calculs se fassent à la main.

Avant de commencer, rappelons qu'une probabilité est toujours un nombre compris entre 0 et 1. On peut l'écrire sous forme de fraction, de nombre décimal ou de pourcentage : $\frac{1}{4}$, $0{,}25$ et $25\ \%$ désignent la même probabilité. Une probabilité de 0 correspond à un événement qui ne se produit jamais, une probabilité de 1 à un événement certain.

## Expérience aléatoire et univers

Une **expérience aléatoire** est une expérience dont on connaît tous les résultats possibles, mais dont on ne peut pas prévoir lequel se produira. Chaque résultat possible s'appelle une **issue** (ou éventualité).

L'ensemble de toutes les issues s'appelle l'**univers**, noté $\Omega$. Par exemple :
- pour le lancer d'une pièce, $\Omega = \{P ; F\}$ ;
- pour le lancer d'un dé à six faces, $\Omega = \{1 ; 2 ; 3 ; 4 ; 5 ; 6\}$ ;
- pour le tirage d'une carte dans un jeu de 32 cartes, l'univers contient 32 issues.

Il est important de bien choisir l'univers. Si l'on lance deux pièces, on peut noter les issues $PP$, $PF$, $FP$ et $FF$ : l'univers contient alors quatre issues, et non trois. Confondre $PF$ et $FP$ est une erreur classique qui conduit à des probabilités fausses, car les trois résultats « deux piles », « deux faces » et « un de chaque » ne sont pas également probables.

Remarque : l'univers peut être infini, par exemple quand on mesure une durée. Dans ce cours, sauf mention contraire, les univers sont finis.

## Événements

Un **événement** est une partie de l'univers, c'est-à-dire un ensemble d'issues. On dit qu'un événement est réalisé lorsque l'issue obtenue lui appartient.

Pour le lancer d'un dé, l'événement $A$ : « obtenir un nombre pair » est $A = \{2 ; 4 ; 6\}$. L'événement $B$ : « obtenir au moins 5 » est $B = \{5 ; 6\}$.

Quelques événements particuliers :
- l'**événement certain** est l'univers lui-même, $\Omega$ ;
- l'**événement impossible** est l'ensemble vide, noté $\emptyset$ ;
- un **événement élémentaire** ne contient qu'une seule issue, par exemple $\{3\}$.

On combine les événements avec deux opérations. L'**intersection** $A \cap B$ (« $A$ et $B$ ») contient les issues communes à $A$ et à $B$ ; ici $A \cap B = \{6\}$. La **réunion** $A \cup B$ (« $A$ ou $B$ ») contient les issues qui sont dans $A$, dans $B$, ou dans les deux ; ici $A \cup B = \{2 ; 4 ; 5 ; 6\}$.

Deux événements sont **incompatibles** lorsqu'ils ne peuvent pas être réalisés en même temps, c'est-à-dire lorsque $A \cap B = \emptyset$. Par exemple, « obtenir 1 » et « obtenir un nombre pair » sont incompatibles.

## Loi de probabilité sur un univers fini

Définir une **loi de probabilité** sur un univers fini, c'est associer à chaque issue un nombre positif, sa probabilité, de sorte que la somme de toutes ces probabilités soit égale à 1.

La probabilité d'un événement est alors la somme des probabilités des issues qui le composent. Pour un dé truqué dont la face 6 sort avec une probabilité de $0{,}5$ et les cinq autres faces avec une probabilité de $0{,}1$ chacune, la probabilité d'obtenir un nombre pair vaut $0{,}1 + 0{,}1 + 0{,}5 = 0{,}7$.

On peut **estimer** une loi de probabilité par l'expérience : si l'on répète un grand nombre de fois l'expérience, la fréquence d'apparition d'une issue se rapproche de sa probabilité. C'est ce qu'on appelle parfois la loi des grands nombres, que ce cours ne démontre pas.

Remarque : une probabilité ne prédit jamais un résultat isolé. Dire qu'une pièce tombe sur pile avec une probabilité de $\frac{1}{2}$ ne signifie pas qu'on obtiendra exactement 5 piles en 10 lancers.

## Équiprobabilité

Lorsque toutes les issues ont la même probabilité, on dit qu'il y a **équiprobabilité**. C'est le cas d'un dé équilibré, d'une pièce bien faite ou d'un tirage « au hasard » dans une urne où les boules sont indiscernables au toucher.

Dans ce cas, la probabilité d'un événement $A$ se calcule par la formule :

$$P(A) = \frac{\text{nombre d'issues favorables à } A}{\text{nombre total d'issues}}$$

Exemple : on lance deux dés équilibrés et on s'intéresse à la somme des faces. L'univers adapté est celui des 36 couples $(a ; b)$, tous équiprobables. La somme vaut 7 pour les six couples $(1;6)$, $(2;5)$, $(3;4)$, $(4;3)$, $(5;2)$ et $(6;1)$, donc la probabilité d'obtenir une somme de 7 est $\frac{6}{36} = \frac{1}{6}$.

Attention : la somme des deux dés n'est pas équiprobable. Il n'y a qu'un couple qui donne 2, mais six qui donnent 7. C'est pourquoi il faut raisonner sur les couples et non sur les sommes.

## Propriétés de calcul

Pour tous événements $A$ et $B$ d'un univers fini :
- $P(\emptyset) = 0$ et $P(\Omega) = 1$ ;
- $0 \le P(A) \le 1$ ;
- $P(A \cup B) = P(A) + P(B) - P(A \cap B)$.

Si $A$ et $B$ sont incompatibles, la dernière formule devient simplement $P(A \cup B) = P(A) + P(B)$.

Exemple : dans une classe de 40 élèves, 18 font du football, 12 font du basket et 5 font les deux. On choisit un élève au hasard. La probabilité qu'il pratique au moins un de ces deux sports est $\frac{18}{40} + \frac{12}{40} - \frac{5}{40} = \frac{25}{40} = 0{,}625$.

On retire $P(A \cap B)$ parce que les élèves qui font les deux sports ont été comptés deux fois. Oublier ce terme est l'erreur la plus fréquente dans les exercices de ce type.

## Dénombrement simple

Pour calculer des probabilités en situation d'équiprobabilité, il faut savoir compter les issues. Trois outils suffisent dans ce cours.

Le **principe multiplicatif** : si une expérience se fait en deux étapes, avec $n$ résultats possibles pour la première et $m$ pour la seconde, il y a $n \times m$ résultats en tout. Ainsi, un code formé d'une lettre (26 choix) suivie d'un chiffre (10 choix) peut prendre $260$ valeurs.

Le **tableau à double entrée** est pratique pour deux dés ou deux tirages : on place les résultats du premier en ligne, ceux du second en colonne, et chaque case est une issue.

L'**arbre de dénombrement** représente les étapes successives. Chaque chemin de la racine à une feuille est une issue. Par exemple, pour trois lancers de pièce, l'arbre a $2 \times 2 \times 2 = 8$ feuilles.

On distingue les tirages **avec remise**, où l'objet tiré est remis avant le tirage suivant, et **sans remise**. Dans une urne de 5 boules, deux tirages avec remise donnent $5 \times 5 = 25$ issues, deux tirages sans remise en donnent $5 \times 4 = 20$.

## Arbres pondérés

Lorsque les issues ne sont pas équiprobables, on utilise un **arbre pondéré** : sur chaque branche, on écrit la probabilité de passer d'un nœud au suivant.

Les règles sont les suivantes :
- la somme des probabilités des branches issues d'un même nœud vaut 1 ;
- la probabilité d'un chemin est le **produit** des probabilités rencontrées sur ce chemin ;
- la probabilité d'un événement est la **somme** des probabilités des chemins qui le réalisent.

Exemple : un joueur de basket réussit son premier lancer franc avec une probabilité de $0{,}8$. S'il le réussit, il réussit le second avec une probabilité de $0{,}9$ ; s'il le rate, avec une probabilité de $0{,}6$. La probabilité qu'il réussisse les deux est $0{,}8 \times 0{,}9 = 0{,}72$. La probabilité qu'il en réussisse exactement un est $0{,}8 \times 0{,}1 + 0{,}2 \times 0{,}6 = 0{,}08 + 0{,}12 = 0{,}2$.

## Probabilités conditionnelles

Si $A$ est un événement de probabilité non nulle, la **probabilité de $B$ sachant $A$** est :

$$P_A(B) = \frac{P(A \cap B)}{P(A)}$$

Elle mesure la probabilité de $B$ quand on sait déjà que $A$ est réalisé. Dans un arbre pondéré, les probabilités écrites sur les branches du deuxième niveau sont des probabilités conditionnelles.

On en déduit la formule $P(A \cap B) = P(A) \times P_A(B)$, qui justifie la règle du produit sur les chemins.

Deux événements $A$ et $B$ sont **indépendants** lorsque $P(A \cap B) = P(A) \times P(B)$, c'est-à-dire lorsque savoir que $A$ est réalisé ne change pas la probabilité de $B$. Les résultats de deux lancers successifs d'un dé sont indépendants. Les résultats de deux tirages sans remise dans une urne ne le sont pas.

Attention à ne pas confondre « incompatibles » et « indépendants » : deux événements incompatibles de probabilités non nulles ne sont jamais indépendants.

## Simulation et fréquences

Quand un calcul exact est difficile, on peut **simuler** l'expérience : avec un tableur, une calculatrice ou un petit programme, on reproduit un grand nombre de fois l'expérience aléatoire et on observe les fréquences.

Par exemple, pour estimer la probabilité qu'au moins deux élèves d'un groupe de 30 soient nés le même mois, on tire au hasard 30 nombres entre 1 et 12, on regarde s'il y a une répétition, et on recommence 1 000 fois. La fréquence obtenue est une estimation de la probabilité cherchée (ici, elle est très proche de 1, puisqu'avec 30 élèves et 12 mois une répétition est certaine).

Une simulation donne une **valeur approchée**, jamais une valeur exacte. Plus le nombre de répétitions est grand, plus l'estimation est en général précise. Au niveau de ce cours, on admet qu'avec $n$ répétitions, la fréquence observée est, dans environ 95 % des cas, à moins de $\frac{1}{\sqrt{n}}$ de la probabilité. Avec 10 000 répétitions, l'écart est donc le plus souvent inférieur à $0{,}01$.

Les simulations servent aussi à vérifier un calcul : si le calcul donne $0{,}3$ et que la simulation donne $0{,}7$ sur 10 000 essais, il y a très probablement une erreur dans le calcul, ou dans la simulation.

## Répétition d'expériences identiques

On répète plusieurs fois, de façon indépendante, la même expérience à deux issues : le **succès**, de probabilité $p$, et l'**échec**, de probabilité $1 - p$. Une telle expérience s'appelle une **épreuve de Bernoulli**, et sa répétition un **schéma de Bernoulli**.

Avec un arbre, on voit que la probabilité d'obtenir une suite précise de résultats est le produit des probabilités. Par exemple, pour trois tirs avec $p = 0{,}7$, la suite « succès, échec, succès » a une probabilité de $0{,}7 \times 0{,}3 \times 0{,}7 = 0{,}147$.

La probabilité d'obtenir exactement $k$ succès en $n$ répétitions fait intervenir le nombre de chemins de l'arbre qui contiennent $k$ succès. Ce nombre se note $\binom{n}{k}$ et se lit « $k$ parmi $n$ ». On obtient :

$$P(X = k) = \binom{n}{k} p^k (1 - p)^{n - k}$$

Pour trois tirs, il y a $\binom{3}{2} = 3$ chemins avec exactement deux succès, donc $P(X = 2) = 3 \times 0{,}7^2 \times 0{,}3 = 0{,}441$.

Ce cours se contente de cette formule ; l'étude complète de la loi binomiale, de son espérance $np$ et de sa variance $np(1-p)$ est faite au chapitre suivant.

## Erreurs fréquentes

Voici les erreurs que l'on rencontre le plus souvent dans les copies, et comment les éviter.

- **Additionner des probabilités d'événements qui ne sont pas incompatibles** sans retirer l'intersection. Toujours se demander si les deux événements peuvent arriver en même temps.
- **Multiplier des probabilités d'événements qui ne sont pas indépendants.** Dans un tirage sans remise, la composition de l'urne change après le premier tirage : la deuxième probabilité est une probabilité conditionnelle.
- **Mal choisir l'univers**, par exemple en prenant les sommes de deux dés comme issues équiprobables.
- **Trouver une probabilité supérieure à 1** ou négative : c'est le signe certain d'une erreur, à corriger avant de rendre la copie.
- **Confondre $P_A(B)$ et $P_B(A)$.** La probabilité d'être malade sachant que le test est positif n'est pas la probabilité que le test soit positif sachant qu'on est malade.
- **Oublier une branche dans un arbre.** Vérifier que les probabilités des branches issues d'un même nœud ont une somme égale à 1.

Un bon réflexe : relire l'énoncé et écrire en toutes lettres l'événement dont on cherche la probabilité, avant tout calcul.

## L'exemple des trois urnes

Cet exemple sert de fil rouge pour les exercices de fin de chapitre. On dispose de trois urnes :
- l'urne $U_1$ contient **7 boules rouges et 5 boules noires** ;
- l'urne $U_2$ contient 3 boules rouges et 9 boules noires ;
- l'urne $U_3$ contient 6 boules rouges et 6 boules noires.

On lance un dé équilibré : si l'on obtient 1, on tire une boule dans $U_1$ ; si l'on obtient 2 ou 3, dans $U_2$ ; sinon, dans $U_3$. Les probabilités de choisir chaque urne sont donc $\frac{1}{6}$, $\frac{2}{6}$ et $\frac{3}{6}$.

La probabilité de tirer une boule rouge se calcule avec l'arbre pondéré :

$$P(R) = \frac{1}{6} \times \frac{7}{12} + \frac{2}{6} \times \frac{3}{12} + \frac{3}{6} \times \frac{6}{12} = \frac{7 + 6 + 18}{72} = \frac{31}{72}$$

Cette méthode, qui consiste à découper l'univers selon l'urne choisie puis à additionner, s'appelle la **formule des probabilités totales**.

## Variables aléatoires

Une **variable aléatoire** $X$ associe un nombre réel à chaque issue d'une expérience aléatoire. Par exemple, dans un jeu où l'on gagne 5 euros si le dé tombe sur 6 et où l'on perd 1 euro sinon, le gain $X$ prend les valeurs $5$ et $-1$.

La **loi de probabilité** de $X$ donne la probabilité de chacune de ses valeurs. Ici, $P(X = 5) = \frac{1}{6}$ et $P(X = -1) = \frac{5}{6}$.

L'**espérance** de $X$ est la moyenne des valeurs pondérée par leurs probabilités :

$$E(X) = \sum x_i \, P(X = x_i)$$

Pour le jeu ci-dessus, $E(X) = 5 \times \frac{1}{6} + (-1) \times \frac{5}{6} = 0$ : en moyenne, sur un grand nombre de parties, on ne gagne ni ne perd. On dit que le jeu est **équitable**.

La **variance** mesure la dispersion des valeurs autour de l'espérance : $V(X) = E\big((X - E(X))^2\big)$. Son écart-type est $\sigma(X) = \sqrt{V(X)}$.

## Exercices d'application

**Exercice 1.** Une urne contient 4 boules vertes et 6 boules jaunes. On tire successivement deux boules sans remise. Construire l'arbre pondéré, puis calculer la probabilité d'obtenir deux boules de la même couleur.

**Exercice 2.** Dans un lycée, 60 % des élèves sont des filles. Parmi les filles, 30 % pratiquent un sport en club ; parmi les garçons, 45 %. On choisit un élève au hasard. Calculer la probabilité qu'il pratique un sport en club, puis la probabilité que ce soit une fille sachant qu'il pratique un sport en club.

**Exercice 3.** On lance trois fois un dé équilibré. Calculer la probabilité de n'obtenir aucun 6, puis celle d'obtenir au moins un 6. Comparer avec la valeur obtenue par une simulation de 1 000 lancers triples.

**Exercice 4.** Un jeu coûte 2 euros. On lance deux dés : si la somme vaut 12, on gagne 30 euros ; si la somme vaut 7, on gagne 6 euros ; sinon on ne gagne rien. On note $X$ le gain algébrique (gain moins mise). Donner la loi de $X$, calculer son espérance et dire si le jeu est favorable au joueur.

**Exercice 5.** En reprenant l'exemple des trois urnes, calculer la probabilité que la boule ait été tirée dans l'urne $U_3$ sachant qu'elle est rouge.

**Exercice 6.** Un test de dépistage est positif chez 98 % des personnes malades et chez 3 % des personnes saines. La maladie touche 1 % de la population. Calculer la probabilité qu'une personne dont le test est positif soit réellement malade, et commenter le résultat.

**Exercice 7.** Un code de cadenas est formé de trois chiffres. Combien de codes différents existe-t-il ? Combien ont trois chiffres distincts ? Quelle est la probabilité qu'un code choisi au hasard ait trois chiffres distincts ?

**Exercice 8.** Un archer atteint la cible avec une probabilité de $0{,}6$ à chaque tir, de façon indépendante. Il tire cinq flèches. Calculer la probabilité qu'il atteigne la cible exactement trois fois, puis au moins une fois.

Pour chaque exercice, rédiger la solution en nommant les événements, en justifiant le choix de la méthode (arbre, tableau, formule) et en vérifiant la cohérence du résultat.

## Méthodes et règle du complément

Pour finir, voici les méthodes à retenir pour les exercices.

1. Toujours commencer par décrire l'univers et vérifier s'il y a équiprobabilité.
2. Pour une expérience en plusieurs étapes, faire un arbre ou un tableau.
3. Pour un événement du type « au moins un », utiliser la **règle du complément** : $P(\text{au moins un}) = 1 - P(\text{aucun})$. L'événement contraire de $A$, noté $\overline{A}$, vérifie toujours $P(\overline{A}) = 1 - P(A)$.

Exemple : on lance quatre fois une pièce équilibrée. La probabilité d'obtenir au moins un pile est $1 - \left(\frac{1}{2}\right)^4 = 1 - \frac{1}{16} = \frac{15}{16}$. Compter directement tous les cas avec au moins un pile serait beaucoup plus long.

4. Vérifier à la fin que chaque probabilité trouvée est entre 0 et 1, et que les probabilités d'une loi ont bien une somme égale à 1.
