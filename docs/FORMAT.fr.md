# Le format des macros FFXI, tel qu'observé

[English](FORMAT.md) · [Français](FORMAT.fr.md) — retour au [README](../README.fr.md)

Tout ce qui suit a été reconstitué depuis une installation réelle et vérifié sur de vrais fichiers,
pas deviné. C'est la référence derrière la seule promesse ferme de l'éditeur : ce qu'il réécrit est
ce que le jeu avait écrit, à l'octet près.

# Format binaire — vérifié sur données réelles

Tout ce qui suit a été confirmé sur 493 fichiers de 5 personnages, pas seulement déduit de la
décompilation. Les points ⚠️ corrigent la spec d'origine.

## En-tête commun (24 octets)

Partagé par `mcr*.dat`, `mcr*.ttl` et `mcr.sys` :

| Offset | Taille | Champ |
|---:|---:|---|
| 0 | 8 | Version / stamp. Mot bas toujours `1`, mot haut variable selon l'install. **Recopié tel quel.** |
| 8 | 16 | MD5 de tout ce qui suit. **Recalculé à chaque écriture.** |

Le MD5 stocké correspondait aux données dans **503 fichiers sur 503**.

## Fichier de macros `mcr<N>.dat` — 7624 octets

24 octets d'en-tête + 7600 octets de données = 20 macros × 380 octets.

Chaque macro (380 octets) :

| Offset | Taille | Champ | Observé |
|---:|---:|---|---|
| 0 | 4 | réservé / flags | `00 00 00 00` dans les 9860 macros lues → recopié tel quel |
| 4 | 61 | ligne 1 | texte + padding `0x00`, **60 octets utiles max** (le 61ᵉ est toujours nul) |
| 65…309 | 61×5 | lignes 2 à 6 | idem |
| 370 | 9 | nom | texte + padding, **8 octets utiles max** |
| 379 | 1 | réservé | `0x00` partout → recopié tel quel |

Longueurs réellement observées : ligne max 55 octets, nom max 8 octets.

## ⚠️ 40 books × 10 sets, pas 20 books

La spec annonçait « 20 books par perso ». La réalité, confirmée par un dossier contenant les 400
fichiers :

- un dossier de perso contient jusqu'à **400 fichiers** : `mcr.dat` (index 0) puis `mcr1.dat` …
  `mcr399.dat` ;
- `index = (book − 1) × 10 + (set − 1)` → **40 books de 10 sets de 20 macros** ;
- les fichiers sont créés à la demande par le jeu, donc la plupart des dossiers en contiennent bien
  moins (ici : 400, 66, 14, 12 et 1) ;
- vérification indépendante : les dates de modification se groupent par dizaines (140-149, 190-199)
  et les fichiers de titres contiennent exactement 40 noms au total.

## ⚠️ Titres : deux fichiers `.ttl` de 20 titres

La spec parlait d'« un tableau de ~10 noms ». En réalité :

- `mcr.ttl` → books 1-20, `mcr_2.ttl` → books 21-40 ;
- 344 octets = 24 d'en-tête + **20 champs de 16 octets** (15 octets utiles) ;
- le jeu écrit `Book01` … `Book40` pour un book sans titre.

`mcr.sys` (28 octets = en-tête + 4 octets de données) n'est pas encore interprété — il est laissé
intact.

## ⚠️ Auto-traduction : présente dès la v1, jamais perdue

Les phrases d'auto-traduction sont stockées **à l'intérieur** des lignes, sous la forme
`FD b1 b2 b3 b4 FD` (6 octets) — 422 occurrences dans le corpus, p. ex.
`/ja "<FD 02 02 1F 97 FD>" <t>`. Un simple passthrough ASCII les détruirait à la sauvegarde.

`FfxiText` utilise donc une forme texte **sans perte** dès le jalon 1 :

| Forme éditable | Octets sur disque |
|---|---|
| `/ja "Provoke" <t>` | ASCII 0x20-0x7E |
| `«02021F97»` ou `«Provoke»` | `FD 02 02 1F 97 FD` |
| `{00}`, `{9E}`, … | n'importe quel autre octet |
| `{{` | `{` littéral |

### Noms lisibles des phrases (livré en avance sur le jalon 5)

La structure exacte, confirmée sur les 108 séquences distinctes du corpus, est
`FD <table> 02 <id sur 16 bits, gros-boutiste> FD` :

| `<table>` | Contenu | Séquences du corpus |
|---|---|---|
| `0x02` | liste d'auto-traduction (Provoke, Savage Blade, Haste Samba…) | 105 |
| `0x07` | liste d'objets (Forbidden Key, Panacea, Foil) | 3 |

Les noms viennent **des fichiers du jeu lui-même** (voir le jalon 5 plus bas). Une installation
**Windower**, si elle est présente, complète ce que le client garde sous forme de marqueurs (noms de
lieux et de jobs) et fournit les objets. Sans ni l'un ni l'autre, tout reste en `«02021F01»` — moins
lisible, jamais faux.

| Forme éditable | Signification |
|---|---|
| `«Provoke»` | phrase d'auto-traduction |
| `«item Forbidden Key»` | objet |
| `«Vallation#1FF2»` | nom que le jeu réutilise (ici les id 8156 et 8178) : l'id est écrit pour lever l'ambiguïté |
| `«02021F01»` | phrase inconnue du dictionnaire |

Les chevrons rappellent les crochets que le jeu dessine autour d'une phrase d'auto-traduction. **Le
jeu ne voit jamais cette notation** : c'est uniquement l'affichage de l'éditeur, et l'enregistrement
réécrit les 6 octets `FD 02 02 xx xx FD` d'origine. La forme `{AT:Provoke}` reste acceptée en saisie,
pour qui n'a pas `«` sous la main.

Un nom réutilisé ne l'est pas forcément deux fois : « Animated Flourish » couvre les id 8094, 8095
et 8117. Écrire l'id sur chacun remplissait les macros de `«Animated Flourish#1F9E»` pour rien —
**le premier id garde donc le nom nu** (c'est celui que le menu du jeu insère), et seuls les
suivants portent leur id, parce que c'est la seule façon de préserver leurs octets.

**La garantie d'exactitude ne dépend pas du dictionnaire** : un nom n'est écrit que si le ré-encoder
redonne exactement les mêmes octets — sinon la forme hexadécimale l'emporte. Le round-trip sur les
493 fichiers réels reste identique octet par octet, dictionnaire chargé ou non.

Reste au jalon 5 : lire les mêmes tables depuis `VTABLE.DAT` / `FTABLE.DAT` de l'installation du jeu,
pour ne plus dépendre de Windower.

## ⚠️ 52 lignes corrompues

Dans le corpus, 52 lignes du personnage `a1b2c3d` ont leur `/` initial **remplacé par un octet
`0x00`** (`{00}con send Kaelith "Healing Waltz" <laststid>`), parfois suivies de restes d'une ligne
plus longue après le terminateur. Le jeu s'arrête au premier `0x00` : **ces lignes ne font rien en
jeu**.

Un décodage naïf « couper au premier `0x00` » perdrait silencieusement le reste de la ligne à la
sauvegarde. Le décodeur conserve donc les octets nuls internes et les affiche en `{00}` — visible,
réparable, et l'aller-retour reste exact.

**L'interface montre partout ce que le jeu montre**, noms comme lignes : un champ stocké
`Palis{00}el` se lit `Palis`, et `/ta <stpc>{00}" <t>` se lit `/ta <stpc>`. Les octets morts ne sont
pas affichés, et **ils sont supprimés du fichier au premier enregistrement du set** — le jeu ne les
lisait pas, les retirer ne change donc rien en jeu, mais le fichier cesse de traîner les restes de
un ancien outil.

Une exception délibérée : un champ dont le **premier** octet est nul (`{00}con send …`) n'est jamais
nettoyé automatiquement. Le jeu n'y exécute rien, donc il n'y a pas de déchet à enlever — seulement
du texte récupérable, et le supprimer en silence le perdrait. C'est le bouton **Réparer** qui le
restaure, en remettant le `/` initial : cette opération-là change ce que le jeu exécute, elle reste
donc manuelle.

---

# Découverte disque (jalon 2)

## Détection du dossier `USER`

Aucun chemin n'est codé en dur. `UserFolderLocator.Detect()` sonde, dans l'ordre :

1. la variable d'environnement `FFXI_USER_DIR`, puis le dossier mémorisé dans `settings.json` ;
2. les installs **PlayOnline** : `PlayOnline\SquareEnix\FINAL FANTASY XI\USER` et la variante
   `PlayOnlineViewer\…`, sous `Program Files`, `Program Files (x86)` et à la racine de chaque disque
   fixe ;
3. les installs **Steam** : lecture de `steamapps\libraryfolders.vdf` (parseur KeyValues maison) pour
   énumérer toutes les bibliothèques, puis recherche de `SquareEnix\FINAL FANTASY XI\USER` dans
   **chaque** dossier de `steamapps\common` — et pas seulement dans `FFXIPAL`, car le dossier est
   parfois renommé ;
4. un balayage de surface des disques fixes (`<disque>\…`, `<disque>\Games\…`, `<disque>\FFXIPAL\…`).

Les candidats sont classés par nombre de personnages puis par date d'activité. Rien ne lève
d'exception : un disque illisible ou un `.vdf` tronqué est journalisé et ignoré.

Sur la machine de développement, la détection trouve l'install Steam réelle **et** une copie du
dossier de jeu — d'où le choix de balayer tout `steamapps\common` :

```
D:\Steam\steamapps\common\FFXIPAL\SquareEnix\FINAL FANTASY XI\USER          5 personnages
D:\Steam\steamapps\common\FFXIPAL - Copie\SquareEnix\FINAL FANTASY XI\USER  1 personnage
```

`Resolve()` est tolérant sur ce que l'utilisateur sélectionne dans un explorateur de dossiers : le
dossier `USER`, le dossier de jeu au-dessus, ou même un dossier de personnage — tous ramènent au bon
`USER`.

## Confirmation du mapping book/set sur données réelles

Le listing d'un personnage joué confirme `index = (book−1)×10 + (set−1)` : les books effectivement
utilisés ont leurs 10 sets, les autres n'ont que le premier.

```
Book  1  RdmBlm   sets [1234567890]
Book  3  CorDnc   sets [12345..890]
Book  7  Book07   sets [1.........]
```

## Réglages persistants

`%APPDATA%\FfxiMacroEditor\settings.json` : dossier `USER` courant, dossiers récents, mapping
`id hexadécimal → nom lisible`, dossier de sauvegarde, options de log. Un fichier corrompu n'est
jamais fatal : il est signalé et remplacé à la prochaine écriture.

## Journalisation

`IMacroLog` (fichier, console, ou les deux) remplace les erreurs avalées de l'ancien outil. Sont
journalisés : chaque candidat `USER` trouvé, chaque bibliothèque Steam, chaque fichier ignoré et
pourquoi, chaque fichier de taille anormale, chaque `.ttl` illisible. `--debug` écrit
`%APPDATA%\FfxiMacroEditor\ffxi-macro-editor.log`.

## Ce que le scan ignore, en le disant

- les fichiers `mcr*.dat` qui ne suivent pas le nommage du jeu (`mcrx.dat`, `mcr07.dat`) → listés
  dans `CharacterFolder.SkippedFiles` et journalisés « not mcr#.dat » ;
- les fichiers macro dont la taille n'est pas 7624 octets → signalés, marqués
  `HasExpectedSize = false`, jamais chargés en silence ;
- les sous-dossiers de `USER` sans fichier macro ;
- un dossier de personnage au nom non hexadécimal est **conservé** (avec une note) plutôt que rejeté.

## Sauvegarde

`MacroLibrary.BackupCharacter` copie uniquement `mcr*.dat`, `mcr*.ttl` et `mcr.sys` dans
`Backups\<id>-<horodatage>\` — jamais le dossier entier, qui contient des mégaoctets de données de
jeu sans rapport.

## Garde-fous implémentés

- refus de lire un fichier ≠ 7624 octets (ou ≠ 344 pour un `.ttl`), avec un message explicite ;
- refus d'écrire si le bloc de données ≠ 7600 octets ;
- refus d'écrire une ligne > 60 octets ou un nom > 8 octets — ou troncature propre à la demande,
  sans jamais couper une phrase d'auto-traduction en deux ;
- écriture atomique (fichier temporaire puis remplacement) ;
- chemins longs Windows gérés via le préfixe `\\?\`, avec une vraie erreur si ça échoue quand même ;
- aucune dépendance Windows exotique (pas de « East Asian language support »).

---

# Édition avancée (jalon 4)

## Recherche

Le champ **Rechercher…** balaie tout le dossier `USER` : lignes de commande, noms de macro et titres
de books, sans tenir compte de la casse. Chaque résultat indique sa position exacte
(`Kaelith · Book 15 « PldRunR » · Set 1 · Ctrl-2 · ligne 1`) et un clic ouvre directement la macro
concernée. La recherche s'arrête à 500 résultats pour qu'un mot courant ne noie pas la liste.

## Copier / déplacer

| Geste | Effet |
|---|---|
| glisser une macro sur une autre | **échange** les deux emplacements |
| `Ctrl` + glisser une macro | **copie** sur la destination |
| clic droit sur une macro | Copier / Coller / Vider (le presse-papier traverse les sets et les persos) |
| glisser un book sur un autre | **déplace** le book (10 sets + titre) |
| `Ctrl` + glisser un book | **copie** le book |

Une macro s'échange plutôt que d'écraser : rien n'est perdu par un geste malheureux, et `F5` recharge
le set depuis le disque de toute façon.

Un déplacement de book écrase dix fichiers d'un coup, donc **il n'est jamais appliqué directement** :
une barre de confirmation annonce précisément ce qui va se passer (« … 3 set(s) du book de
destination seront écrasés, et le book d'origine sera vidé »). Les deux personnages concernés sont
sauvegardés avant écriture, et l'opération est refusée tant qu'il reste des modifications non
enregistrées.

Les titres suivent : copier un book copie son titre, le déplacer vide le titre d'origine, et le bon
`.ttl` (`mcr.ttl` ou `mcr_2.ttl` selon le numéro de book) est réécrit.

## Import / export

**Exporter…** écrit le set courant en `.txt` lisible ou en `.json` structuré ; **Importer…** relit
l'un ou l'autre dans le set courant, sans enregistrer tant que tu n'as pas cliqué Enregistrer.

```
# FFXI macro set
# Kaelith (a1b2c3d) · book 15 (PldRunR) · set 1

[Ctrl-1] ShieldBa
/ja "«Shield Bash»" <stnpc>

[Ctrl-2] Flash
/ma "«Flash»" <stnpc>
```

Les deux formats font l'aller-retour à l'octet près, y compris les cas piégeux : une macro qui laisse
la ligne 2 vide et utilise la ligne 3 garde ses positions, et un nom dont l'espace final compte
(`"Box "`) est mis entre guillemets pour survivre à un éditeur de texte.

## Réparation

**Réparer** remet le `/` initial que un ancien outil avait remplacé par un octet nul — ce qui
réveille une ligne que le jeu ignorait, d'où le fait que ce soit un geste explicite. Les simples
restes de ligne après le terminateur, eux, disparaissent tout seuls au premier enregistrement. Rien
n'est écrit tant que tu n'enregistres pas.

---

# Lire les tables du jeu (jalon 5)

Le but de ce jalon : ne plus dépendre d'un outil tiers pour afficher les phrases d'auto-traduction.
Tout ce qui suit a été reconstitué sur l'installation réelle, puis vérifié contre une source
indépendante.

## `VTABLE.DAT` + `FTABLE.DAT` — l'index des fichiers de données

La spec ne connaissait qu'une contrainte : « FTABLE fait exactement 2× la taille de VTABLE ».
Vérifiée (219 402 / 109 701), et voici pourquoi :

- `VTABLE.DAT` : **un octet par identifiant** — le numéro de volume ROM, ou 0 si l'identifiant est
  inutilisé ;
- `FTABLE.DAT` : **deux octets par identifiant** (petit-boutiste) — le dossier sur les 9 bits hauts,
  le fichier sur les 7 bits bas.

D'où `ROM<volume>/<packed >> 7>/<packed & 0x7F>.DAT`. Sur l'installation de test : 109 701
identifiants, dont 83 116 utilisés, et **tous les 83 116 pointent vers un fichier qui existe**.

## Le dictionnaire d'auto-traduction

Il se trouve dans `ROM/168/25.DAT`, et son format est auto-descriptif :

```
02 02 <groupe> <index> <longueur> <texte…> 00
```

Les deux premiers octets sont exactement ceux qu'une macro stocke entre ses marqueurs `FD`, et
**l'identifiant d'une phrase est simplement `(groupe << 8) | index`** — ce qui confirme, depuis les
données du jeu, ce que j'avais déduit des macros au jalon 3. Un enregistrement d'index 0 ouvre un
groupe : c'est un bloc fixe de 76 octets portant le nom de la catégorie (【Greetings】,
【Job Abilities】…).

Résultat du parseur sur le fichier réel : **2685 phrases, 42 groupes, et l'analyse s'arrête
exactement sur le dernier octet du fichier**.

## Les marqueurs du client

Beaucoup de phrases ne sont pas stockées en clair mais sous forme de marqueur que le client
remplace à l'exécution :

| Marqueur | Contenu | Table | Résolu |
|---|---|---|---|
| `@Y<hex>` | capacités, traits, techniques, ordres de familier | `ROM/181/72.DAT` (5888 entrées) | ✅ **713 / 713** |
| `@C<hex>` | sorts, magie bleue | `ROM/181/73.DAT` (1024 entrées) | ✅ **311 / 311** |
| `@A<hex>` | noms de lieux | table non décodée | ❌ |
| `@J<hex>` | noms de jobs | table non décodée | ❌ |

Ces tables sont au format `d_msg` : en-tête de 64 octets, entrées de taille fixe, texte à +40.
Le marqueur est un index direct : `@Y22E` = entrée 0x22E = 558 = « Shield Bash ».

Les deux catégories non résolues (259 phrases) sont des noms de lieux et de jobs — on n'en met
pratiquement jamais dans une macro, et elles retombent proprement sur la forme hexadécimale. Les
**objets** ne sont pas dans une table `d_msg` (format différent) et restent couverts par Windower.

## Validation

Le décodage a été confronté à une source totalement indépendante — les fichiers de ressources de
Windower, eux-mêmes extraits du jeu par un autre outil :

- **1252 phrases identiques** au caractère près ;
- **1024 marqueurs sur 1024** (`@Y` + `@C`) résolus vers le nom attendu ;
- **2 écarts**, tous deux dus à l'échappement des guillemets dans mon script de comparaison, pas au
  décodage.

## Ce qui est robuste au temps

Les identifiants de fichiers (55665 pour le dictionnaire, 55701 et 55702 pour les tables de noms) ne
sont qu'un **point de départ** : chaque fichier est validé par son contenu. Si une mise à jour du jeu
les déplace, le chargeur balaie l'installation et retrouve les bons fichiers — le dictionnaire par sa
signature, les tables de noms en les notant sur leur capacité à résoudre les marqueurs réellement
présents. Le repérage des 313 tables `d_msg` d'une installation prend environ 5 secondes.

Et si rien de tout cela n'aboutit, l'éditeur affiche `«02021F01»` et continue — c'est exactement le
repli propre que demandait la spec, sans le plantage de l'outil d'origine.

---

# Éditer pendant que le jeu tourne

**Le client ne détient que le book affiché à l'écran.** C'est la règle, mesurée puis confirmée en
jeu : il lit les macros d'un book sur le disque au moment où tu bascules dessus, et n'en garde en
mémoire que celui en cours. Preuve par la mémoire du client, connecté :

```
book 1  (ThfRdm)   con gs c smartbuff              absent de la mémoire
book 36 (BrdDncC)  con sm all follow Kaelith       PRÉSENT   ← le book affiché
book 2  (ThfGeo)   con gs c cycle altPlayerLight   absent
```

Conséquences pratiques :

- **modifier un book sur lequel tu n'es pas → fonctionne à chaud.** Tu enregistres, tu bascules
  dessus en jeu (ou tu changes de job, ce que fait un macrochanger) et la modification est active.
  Vérifié en jeu ;
- **modifier le book affiché → perdu.** Le client possède sa copie et la réécrit par-dessus.
  Constaté : une ligne enregistrée à 18:34 est revenue une seconde plus tard avec l'ancien contenu
  et le tampon de version du client.

L'éditeur affiche donc un bandeau nommant les personnages connectés et rappelant la règle, mais
n'empêche plus l'enregistrement — seul le book sous tes yeux est en jeu.

**Pour modifier le book affiché sans fermer le jeu : l'écran de sélection de personnage suffit.**
Vérifié chronomètre en main :

```
18:50:29   le client écrit mcr.ttl, mcr_2.ttl, mcr.sys   ← déconnexion : il vide ses macros
18:50:47   l'éditeur écrit mcr350.dat                     ← sauvegarde, 18 s plus tard
```

La ligne ajoutée était bien dans le fichier, et présente en jeu à la reconnexion. L'éditeur
reconnaît d'ailleurs cet état : le titre de la fenêtre FFXI vaut le nom du personnage en jeu et
« Final Fantasy XI » à l'écran de sélection, donc le bandeau disparaît dès que tu te déconnectes.

Modifier les macros **pendant qu'on joue** reste hors de portée d'un éditeur externe : il faudrait
écrire dans la mémoire du client, ce que font les addons Windower. C'est un projet d'une autre
nature, et la spec d'origine plaçait déjà l'édition en jeu en temps réel hors du périmètre.
