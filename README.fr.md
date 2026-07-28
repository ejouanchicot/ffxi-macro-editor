# FFXI Macro Editor

[English](README.md) · **Français**

Tes 800 macros, sur un seul écran. Éditables comme du texte, pas comme un menu.

**[Télécharger pour Windows →](https://github.com/ejouanchicot/ffxi-macro-editor/releases/latest)**
Un seul fichier, 27 Mo, rien à installer.

---

## À quoi ça sert

Final Fantasy XI te donne 40 books × 10 sets × 20 macros. En jeu, tu y accèdes par un menu, six
lignes à la fois, une macro à la fois. Monter un nouveau job, c'est une soirée. Renommer un sort
dans tout un book, c'est pire.

Ici tout s'ouvre dans une fenêtre : tu choisis un book à gauche, les vingt macros du set sont
disposées exactement comme les rangées `Ctrl` et `Alt` du jeu, et celle sur laquelle tu cliques
s'ouvre dans un éditeur avec ses six lignes. Tu tapes, tu enregistres, c'est fait.

## Ce que ça t'apporte

**Les phrases d'auto-traduction en clair.** Dans les fichiers bruts, `Provoke` est six octets de
binaire. Ici ça se lit `«Provoke»`, et `Ctrl+Espace` ouvre une recherche : tape `mighty`, clique sur
`Mighty Strikes`, ça atterrit à ton curseur. Le jeu l'affiche entre ses crochets habituels, dans la
langue de ton client. Aucun autre outil nécessaire — les noms sont lus dans ton installation de FFXI.

**Éditer pendant que tu joues.** Le jeu ne retient que le book affiché à l'écran. Tous les autres
sont relus sur le disque au moment où tu bascules dessus : tu peux donc les réécrire en pleine
session, et la modification est active dès que tu changes de book ou de job. Sans redémarrer, sans
te déconnecter.

**Chercher partout d'un coup.** Un seul champ, tous les books de tous les personnages de la machine :
lignes de commande, noms de macro, titres de books. Chaque résultat dit exactement où il se trouve,
et un clic t'y emmène. Précieux le jour où un addon change le nom d'une commande.

**Déplacer les choses.** Glisse une macro sur une autre pour les échanger, `Ctrl` + glisser pour
copier. Glisse un book entier sur un autre pour déplacer ses dix sets et son titre avec — d'un
personnage à l'autre aussi. Une copie de book demande confirmation, parce qu'elle écrase dix
fichiers.

**Exporter un set** en texte ou en JSON : le versionner, l'envoyer à un ami, le réimporter. L'aller-
retour est exact, trous et espaces finaux compris.

**Réparer ce qui est cassé.** Une macro qui a perdu son `/` initial ne fait rien en jeu et paraît
pourtant normale dans le menu. L'éditeur les repère et remet le slash.

## Pour démarrer

1. [Télécharge `FfxiMacroEditor.exe`](https://github.com/ejouanchicot/ffxi-macro-editor/releases/latest)
   et lance-le. Il est autonome — pas de .NET, pas de runtime, pas d'installeur.
2. Windows affichera **« Windows protected your PC »**, parce que le fichier n'est pas signé.
   *Informations complémentaires → Exécuter quand même.* La page de release publie un SHA-256 si tu
   veux vérifier ton téléchargement.
3. Il trouve ton dossier `USER` tout seul, installations PlayOnline comme Steam. S'il se trompe,
   **Dossier USER…** en bas à gauche le règle, et c'est mémorisé.

Raccourcis : `Ctrl+S` enregistrer, `Ctrl+Maj+S` tout enregistrer, `F5` remettre un set tel qu'il est
sur le disque, `Ctrl+PagePréc` / `Ctrl+PageSuiv` pour parcourir les sets, `Ctrl+Espace` pour les
phrases.

L'interface est en **anglais ou en français** — les boutons `EN` / `FR` dans l'en-tête, sans
redémarrer.

## À propos de tes fichiers

Tes macros sont la trace de beaucoup de soirées, donc :

- **Chaque set est copié avant la première écriture** d'une session, dans
  `%APPDATA%\FfxiMacroEditor\Backups\`. Rien n'est écrasé sans qu'une copie soit posée à côté.
- **Ce qui est réécrit est ce que le jeu avait écrit**, à l'octet près. Ce qui n'est pas compris est
  recopié tel quel plutôt que perdu. Cette garantie est revérifiée sur 493 fichiers réels à chaque
  compilation, et c'est la raison d'être du projet.
- **Rien ne quitte ta machine.** Pas de compte, pas de télémétrie, pas de réseau. Il lit et écrit des
  fichiers dans ton dossier FFXI, et c'est tout ce qu'il fait.
- La seule chose à savoir : **le book ouvert en jeu sera écrasé par le client** si tu l'enregistres.
  L'éditeur affiche un bandeau tant qu'un personnage est connecté, et le nomme. Tous les autres books
  sont à toi.

## Pour les curieux

Le format des fichiers de macros n'est documenté nulle part, alors il a été reconstitué depuis de
vrais fichiers, puis écrit : [le format binaire, tel qu'observé](docs/FORMAT.fr.md) — l'en-tête de
24 octets, les 380 octets par macro, la façon dont une phrase d'auto-traduction est rangée à
l'intérieur d'une ligne, et comment leurs noms sont extraits des tables de données du jeu.

Écrit en C# avec [Avalonia](https://avaloniaui.net/). La bibliothèque qui lit et écrit les fichiers
n'a aucune dépendance à l'interface et tient debout toute seule ; il y a aussi un outil en ligne de
commande.

```bash
dotnet build FfxiMacroEditor.sln
dotnet test  FfxiMacroEditor.sln     # 337 tests
```

---

## Licence

[MIT](LICENSE).

Les noms de personnage et identifiants de dossier utilisés dans la documentation et les échantillons
de test (`Kaelith`, `Sylvane`, `a1b2c3d`) sont des substituts.

Ce projet n'est ni affilié à Square Enix ni approuvé par lui. Il lit et écrit les fichiers de macros
d'une copie du jeu légalement installée, sur ta propre machine. Aucune donnée du jeu n'est
redistribuée : les noms d'auto-traduction sont lus à l'exécution dans l'installation que tu as déjà.
