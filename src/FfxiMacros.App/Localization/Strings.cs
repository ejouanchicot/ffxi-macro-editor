namespace FfxiMacros.App.Localization;

/// <summary>
/// Every word the interface says, in each language it says it in.
/// </summary>
/// <remarks>
/// <para>
/// The two tables are kept side by side rather than in separate files so that adding a label means
/// writing both lines at once; a test compares the key sets and the <c>{0}</c> placeholders, so a
/// forgotten translation fails the build rather than showing up as an English word in a French
/// window.
/// </para>
/// <para>
/// The library's own messages — malformed files, refused writes — stay in English. They are
/// technical, they are what ends up in the log, and a UI-free library has no business carrying an
/// interface language.
/// </para>
/// <para>
/// The <c>«</c> guillemets around an auto-translate phrase are not text: they are part of the
/// editable notation for a phrase, and they read the same in both languages.
/// </para>
/// </remarks>
internal static class Strings
{
    public static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ---------------------------------------------------------------- header
        ["Header.Search"] = "Search",
        ["Header.Search.Tip"] = "Search macro lines, macro names and book titles",
        ["Header.Phrases"] = "Phrases",
        ["Header.Phrases.Tip"] = "Ctrl+Space — insert an auto-translate phrase into the line you are editing",
        ["Header.Language.Tip"] = "Interface language",

        // ---------------------------------------------------------------- books panel
        ["Books.Title"] = "BOOKS",
        ["Books.ChooseFolder"] = "USER folder…",
        ["Books.Refresh"] = "Refresh",
        ["Books.Dirty.Tip"] = "Unsaved changes",
        ["Books.SetsUsed.Tip"] = "Sets in use",
        ["Books.NoFolder"] = "No USER folder selected",

        // ---------------------------------------------------------------- the set being edited
        ["Set.Save"] = "Save",
        ["Set.Save.Tip"] = "Ctrl+S",
        ["Set.SaveAll"] = "Save all",
        ["Set.SaveAll.Tip"] = "Ctrl+Shift+S",
        ["Set.Reload"] = "Reload",
        ["Set.Reload.Tip"] = "F5 — discards the changes to this set",
        ["Set.Repair"] = "Repair",
        ["Set.Repair.Tip"] = "Fix the lines an older tool broke",
        ["Book.BackupAll"] = "Back up everything",
        ["Book.BackupAll.Tip"] = "Every set of every book of every character, and the titles, in one archive",
        ["Book.Backup"] = "Back up this book",
        ["Book.Backup.Tip"] = "Pack this book's own mcr*.dat files into one archive, byte for byte",
        ["Book.Restore"] = "Restore",
        ["Book.Restore.Tip"] = "Put such an archive back into this book, sets and title",
        ["Set.Export"] = "Export text",
        ["Set.Export.Tip"] = "Export as readable .txt or .json — for reading and sharing, not for keeping",
        ["Set.Import"] = "Import text",
        ["Set.Import.Tip"] = "Import a .txt or .json into this set",
        ["Set.Title"] = "{0}   ·   Book {1} “{2}”   ·   Set {3}/10",
        ["Set.PickBook"] = "Pick a book from the list on the left.",
        ["Set.Wheel.Tip"] = "The order the game's up and down arrows walk. Ctrl+PageUp / Ctrl+PageDown.",
        ["Set.Copy"] = "Copy this set",
        ["Set.Paste"] = "Paste a set here",
        ["Set.Clear"] = "Empty this set",

        // ---------------------------------------------------------------- macro editor
        ["Editor.Clear"] = "Clear",
        ["Editor.NameWatermark"] = "Name (8 characters)",
        ["Editor.Copy"] = "Copy this macro",
        ["Editor.Paste"] = "Paste a macro here",

        // ---------------------------------------------------------------- the editor's own clipboard
        ["Clipboard.Empty"] = "Clipboard: empty",
        ["Clipboard.Macro"] = "Clipboard: macro “{0}”",
        ["Clipboard.Set"] = "Clipboard: set {0} of book {1}",
        ["Clipboard.Book"] = "Clipboard: book {0} “{1}”",

        // ---------------------------------------------------------------- search panel
        ["Search.Title"] = "SEARCH",
        ["Search.Watermark"] = "cure, Provoke, exec…",
        ["Search.Close.Tip"] = "Close the search",
        ["Search.NoResult"] = "No result for “{0}”.",
        ["Search.OneResult"] = "1 result",
        ["Search.ManyResults"] = "{0} results",

        // ---------------------------------------------------------------- phrase picker
        ["Phrases.Title"] = "PHRASES",
        ["Phrases.Watermark"] = "mighty, provoke, cure…",
        ["Phrases.IncludeItems"] = "Include items",
        ["Phrases.IncludeItems.Tip"] = "23,000 items: the search is a little slower",
        ["Phrases.Hint"] = "Click in a line, then on a phrase to insert it at the caret.",
        ["Phrases.Item"] = "item",
        ["Phrases.Close.Tip"] = "Close (Ctrl+Space)",
        ["Phrases.NoDictionary"] = "No auto-translate dictionary: neither FFXI nor Windower was found.",
        ["Phrases.Inserted"] = "“{0}” inserted. It takes 6 bytes, whatever its name.",
        ["Phrases.ClickFirst"] = "Click in the line where the phrase should go first.",
        ["Phrases.Known"] = "{0} phrases known",
        ["Phrases.NoneFor"] = "No phrase for “{0}”",
        ["Phrases.One"] = "1 phrase",
        ["Phrases.Many"] = "{0} phrases",

        // ---------------------------------------------------------------- notices
        ["Notice.Confirm"] = "Confirm",
        ["Notice.SaveAndConfirm"] = "Save everything and continue",
        ["Notice.Cancel"] = "Cancel",

        // ---------------------------------------------------------------- the library tree
        ["Tree.Set"] = "Set {0}",
        ["Tree.SetNew"] = "new",
        ["Tree.SetBadSize"] = "invalid size ({0} bytes)",
        ["Tree.BookEmpty"] = "empty",
        ["Tree.SetOne"] = "1 set",
        ["Tree.SetMany"] = "{0} sets",
        ["Tree.BookOne"] = "1 book",
        ["Tree.BookMany"] = "{0} books",
        ["Tree.OpenInGame"] = "where the game left this character, from its own mcr.sys",
        ["Tree.OpenInGameLive"] = "the book open in game, reported by Windower",
        ["Tree.OpenInGameMemory"] = "the book open in game, read from the client itself",
        ["Tree.TitleTail"] =
            "the title field still holds the tail of an older name, which the game does not show; "
            + "renaming this book clears it",
        ["Tree.CharacterDetail"] = "{0}, {1}",
        ["Tree.Skipped"] = "{0} file(s) skipped",

        // ---------------------------------------------------------------- status line
        ["Status.NoChanges"] = "No changes",
        ["Status.DirtyOne"] = "1 set changed",
        ["Status.DirtyMany"] = "{0} sets changed",
        ["Status.NoInstall"] = "No FFXI installation found. Pick the USER folder.",
        ["Status.Detected"] = "Installation found automatically ({0}): {1}",
        ["Status.NoCharacterData"] = "“{0}” holds no character data.",
        ["Status.Md5Mismatch"] = "{0}: the stored MD5 does not match the data. It will be fixed on save.",
        ["Status.Opened"] = "{0} opened.",
        ["Status.Saved"] = "{0} saved.{1}",
        ["Status.SavedBlocked"] = "{0} set(s) saved. {1} blocked: fix the fields marked in red.",
        ["Status.SavedAll"] = "{0} set(s) saved.",
        ["Status.Reloaded"] = "{0} reloaded from disk.",
        ["Status.RewrittenByGame"] = "{0} was rewritten by the game; reloaded from disk.",
        ["Status.ClientUnreadable"] =
            "Windows will not let the editor read {0}: that client runs with more privileges than "
            + "this window. Right-click the editor and « Run as administrator » to see the book it "
            + "has open — everything else works without it.",
        ["Status.SettingsNotSaved"] = "Settings not saved: {0}",
        ["Status.Copied"] = "{0} copied.",
        ["Status.Pasted"] = "Pasted onto {0}.",
        ["Status.CopiedOnto"] = "{0} copied onto {1}.",
        ["Status.Swapped"] = "{0} and {1} swapped.",
        ["Status.NothingToPaste"] = "Nothing to paste: copy a macro, a set or a book first (Ctrl+C).",
        ["Status.SetCopied"] = "Set {0} of book {1} copied — its 20 macros.",
        ["Status.SetPasted"] = "Set {0} of book {1} replaced. Save to apply.",
        ["Status.SetCleared"] = "Set {0} of book {1} emptied. Save to apply, or reload to bring it back.",
        ["Status.SetAlreadyEmpty"] = "Set {0} of book {1} is already empty.",
        ["Status.BookRenamed"] = "Book {0} is now “{1}”." + "",
        ["Status.TitlesNotPushed"] = "The running client did not take the new names of {0}. It may write its own back over them: relog, or reorganise books from the character-select screen.",
        ["Status.EverythingBackedUp"] = "{0} character(s), {1} file(s) backed up to {2}",
        ["Status.BookBackedUp"] = "Book {0} backed up — {1} set file(s) — to {2}",
        ["Status.BookRestored"] = "Book {0} “{1}” restored into book {2}: {3} set(s), the game's own files.",
        ["Status.BookTitleCleared"] = "Book {0} goes back to its own name, and the field is scrubbed.",
        ["Status.CharacterRenamed"] =
            "{0} is shown as “{1}” from now on. That is a label kept by the editor — the folder on "
            + "disk keeps its own name, which is the one the game looks for.",
        ["Status.CharacterUnnamed"] = "{0} goes back to being shown by its folder name.",
        ["Status.LiveUnmatched"] =
            "Windower reports {0} in game on book {1}, but no character here is labelled {0}. "
            + "Select that character in the list, press F2 and type {0}. Nothing is renamed on disk: "
            + "the game finds a character by its folder name, and that must not change.",
        ["Status.BookCleared"] = "Book {0} emptied: its set files are gone and its title is reset.",
        ["Status.NothingToClear"] = "Book {0} is already empty.",
        ["Status.BookOnClipboard"] = "Book {0} “{1}” copied. Paste it onto another book to replace it.",
        ["Status.NothingToRepair"] = "{0}: nothing to repair.",
        ["Status.RepairedOne"] = "1 field repaired ({0}). Save to apply.",
        ["Status.RepairedMany"] = "{0} fields repaired (including {1}). Save to apply.",
        ["Status.ExportedOne"] = "One set exported to {0}.",
        ["Status.Exported"] = "{1} sets exported to {0}.",
        ["Status.ImportedOne"] = "One set imported from {0}. Save to apply.",
        ["Status.Imported"] = "{1} sets imported from {0}. Save to apply.",
        ["Status.Cancelled"] = "Operation cancelled.",
        ["Status.BooksSwapped"] = "Books {0} and {1} swapped.",
        ["Status.BookCopied"] = "Book {0} copied onto book {1}.",
        ["Status.LanguageChanged"] = "Interface language: English.",
        ["Status.CloseWithChanges"] = "{0}. Save (Ctrl+S), or close the window again to quit without saving.",

        // ---------------------------------------------------------------- the game is running
        ["Game.Connected"] =
            "{0} is in game. The client only holds the book shown on screen: that one will be "
            + "overwritten if you save it. Every other book is read from disk the moment you switch "
            + "to it, so you can edit those freely — the change takes effect as soon as you change "
            + "book or job.",
        ["Game.InGame"] = "In game: {0}",
        ["Game.NobodyConnected"] = "Nobody is in game: you can save.",
        ["Game.SaveAdvice"] = " Switch to book {0} in game to see it.",

        // ---------------------------------------------------------------- moving a book
        ["Book.Copy"] = "Copy this book",
        ["Book.Paste"] = "Paste a book here",
        ["Book.Rename"] = "Rename this book (F2)",
        ["Character.Rename"] = "Label this character in the editor (F2)",
        ["Tree.NotHexFolder"] =
            "the game will not find this folder: it looks a character up by the hexadecimal name it "
            + "gave the folder, so a renamed one is invisible to it",
        ["Book.Clear"] = "Empty this book",
        ["Book.OpenInGameWarning"] =
            "Careful: book {0} “{1}” is the one open in game right now. The client holds that one and "
            + "writes its own copy back when it leaves it, so this would be undone. Switch book in game "
            + "first, then do it. — ",
        ["Book.ClearQuestion"] =
            "Empty book {0} “{1}” of {2}? Its {3} set file(s) will be deleted and its title reset. "
            + "Nothing is kept in the editor — only the backup can bring it back.",
        ["Book.SwapQuestion"] = "Swap book {0} “{1}” of {2} with book {3} “{4}” of {5}? They trade places — their macros and their names — and nothing is lost.",
        ["Book.CopyQuestion"] = "Copy book {0} “{1}” from {2} onto book {3} “{4}” of {5}? ",
        ["Book.Overwrites"] = "{0} set(s) of the destination book will be overwritten",
        ["Book.End"] = ".",
        ["Book.NeedsSave"] =
            " First: {0} set(s) hold unsaved edits. Copying a book re-reads every file from disk, "
            + "which would throw them away — so they are saved on the way through.",
    };

    public static readonly IReadOnlyDictionary<string, string> French = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ---------------------------------------------------------------- en-tête
        ["Header.Search"] = "Rechercher",
        ["Header.Search.Tip"] = "Chercher dans les lignes, les noms de macro et les titres de books",
        ["Header.Phrases"] = "Phrases",
        ["Header.Phrases.Tip"] = "Ctrl+Espace — insérer une phrase d'auto-traduction dans la ligne en cours",
        ["Header.Language.Tip"] = "Langue de l'interface",

        // ---------------------------------------------------------------- panneau des books
        ["Books.Title"] = "BOOKS",
        ["Books.ChooseFolder"] = "Dossier USER…",
        ["Books.Refresh"] = "Actualiser",
        ["Books.Dirty.Tip"] = "Modifications non enregistrées",
        ["Books.SetsUsed.Tip"] = "Sets utilisés",
        ["Books.NoFolder"] = "Aucun dossier USER sélectionné",

        // ---------------------------------------------------------------- le set en cours
        ["Set.Save"] = "Enregistrer",
        ["Set.Save.Tip"] = "Ctrl+S",
        ["Set.SaveAll"] = "Tout enregistrer",
        ["Set.SaveAll.Tip"] = "Ctrl+Maj+S",
        ["Set.Reload"] = "Recharger",
        ["Set.Reload.Tip"] = "F5 — annule les modifications de ce set",
        ["Set.Repair"] = "Réparer",
        ["Set.Repair.Tip"] = "Corriger les lignes cassées par un ancien outil",
        ["Book.BackupAll"] = "Tout sauvegarder",
        ["Book.BackupAll.Tip"] = "Tous les sets de tous les books de tous les personnages, et les titres, dans une archive",
        ["Book.Backup"] = "Sauvegarder ce book",
        ["Book.Backup.Tip"] = "Empaqueter les fichiers mcr*.dat de ce book, octet pour octet, dans une archive",
        ["Book.Restore"] = "Restaurer",
        ["Book.Restore.Tip"] = "Remettre une telle archive dans ce book, sets et titre",
        ["Set.Export"] = "Exporter texte",
        ["Set.Export.Tip"] = "Exporter en .txt ou .json lisible — pour lire et partager, pas pour conserver",
        ["Set.Import"] = "Importer texte",
        ["Set.Import.Tip"] = "Importer un .txt ou .json dans ce set",
        ["Set.Title"] = "{0}   ·   Book {1} « {2} »   ·   Set {3}/10",
        ["Set.PickBook"] = "Choisis un book dans la liste de gauche.",
        ["Set.Wheel.Tip"] = "Ordre des flèches haut/bas du jeu. Ctrl+PagePréc / Ctrl+PageSuiv.",
        ["Set.Copy"] = "Copier ce set",
        ["Set.Paste"] = "Coller un set ici",
        ["Set.Clear"] = "Vider ce set",

        // ---------------------------------------------------------------- éditeur de macro
        ["Editor.Clear"] = "Vider",
        ["Editor.NameWatermark"] = "Nom (8 caractères)",
        ["Editor.Copy"] = "Copier cette macro",
        ["Editor.Paste"] = "Coller une macro ici",

        // ---------------------------------------------------------------- presse-papier de l'éditeur
        ["Clipboard.Empty"] = "Presse-papier : vide",
        ["Clipboard.Macro"] = "Presse-papier : macro « {0} »",
        ["Clipboard.Set"] = "Presse-papier : set {0} du book {1}",
        ["Clipboard.Book"] = "Presse-papier : book {0} « {1} »",

        // ---------------------------------------------------------------- panneau de recherche
        ["Search.Title"] = "RECHERCHE",
        ["Search.Watermark"] = "cure, Provoke, exec…",
        ["Search.Close.Tip"] = "Fermer la recherche",
        ["Search.NoResult"] = "Aucun résultat pour « {0} ».",
        ["Search.OneResult"] = "1 résultat",
        ["Search.ManyResults"] = "{0} résultats",

        // ---------------------------------------------------------------- sélecteur de phrases
        ["Phrases.Title"] = "PHRASES",
        ["Phrases.Watermark"] = "mighty, provoke, cure…",
        ["Phrases.IncludeItems"] = "Inclure les objets",
        ["Phrases.IncludeItems.Tip"] = "23 000 objets : la recherche est un peu plus lente",
        ["Phrases.Hint"] = "Clique dans une ligne, puis sur une phrase pour l'insérer au curseur.",
        ["Phrases.Item"] = "objet",
        ["Phrases.Close.Tip"] = "Fermer (Ctrl+Espace)",
        ["Phrases.NoDictionary"] = "Aucun dictionnaire d'auto-traduction : FFXI ou Windower introuvable.",
        ["Phrases.Inserted"] = "« {0} » inséré. Il tient en 6 octets, quel que soit son nom.",
        ["Phrases.ClickFirst"] = "Clique d'abord dans la ligne où insérer la phrase.",
        ["Phrases.Known"] = "{0} phrases connues",
        ["Phrases.NoneFor"] = "Aucune phrase pour « {0} »",
        ["Phrases.One"] = "1 phrase",
        ["Phrases.Many"] = "{0} phrases",

        // ---------------------------------------------------------------- bandeaux
        ["Notice.Confirm"] = "Confirmer",
        ["Notice.SaveAndConfirm"] = "Tout enregistrer et continuer",
        ["Notice.Cancel"] = "Annuler",

        // ---------------------------------------------------------------- arbre de la bibliothèque
        ["Tree.Set"] = "Set {0}",
        ["Tree.SetNew"] = "nouveau",
        ["Tree.SetBadSize"] = "taille invalide ({0} octets)",
        ["Tree.BookEmpty"] = "vide",
        ["Tree.SetOne"] = "1 set",
        ["Tree.SetMany"] = "{0} sets",
        ["Tree.BookOne"] = "1 book",
        ["Tree.BookMany"] = "{0} books",
        ["Tree.OpenInGame"] = "là où le jeu a laissé ce personnage, d'après son propre mcr.sys",
        ["Tree.OpenInGameLive"] = "le book ouvert en jeu, rapporté par Windower",
        ["Tree.OpenInGameMemory"] = "le book ouvert en jeu, lu dans le client lui-même",
        ["Tree.TitleTail"] =
            "le champ du titre garde encore la fin d'un ancien nom, que le jeu n'affiche pas ; "
            + "renommer ce book l'efface",
        ["Tree.CharacterDetail"] = "{0}, {1}",
        ["Tree.Skipped"] = "{0} fichier(s) ignoré(s)",

        // ---------------------------------------------------------------- barre d'état
        ["Status.NoChanges"] = "Aucune modification",
        ["Status.DirtyOne"] = "1 set modifié",
        ["Status.DirtyMany"] = "{0} sets modifiés",
        ["Status.NoInstall"] = "Aucune installation de FFXI trouvée. Choisis le dossier USER.",
        ["Status.Detected"] = "Installation détectée automatiquement ({0}) : {1}",
        ["Status.NoCharacterData"] = "« {0} » ne contient aucune donnée de personnage.",
        ["Status.Md5Mismatch"] = "{0} : le MD5 stocké ne correspond pas aux données. Il sera corrigé à l'enregistrement.",
        ["Status.Opened"] = "{0} ouvert.",
        ["Status.Saved"] = "{0} enregistré.{1}",
        ["Status.SavedBlocked"] = "{0} set(s) enregistré(s). {1} bloqué(s) : corrige les champs en rouge.",
        ["Status.SavedAll"] = "{0} set(s) enregistré(s).",
        ["Status.Reloaded"] = "{0} rechargé depuis le disque.",
        ["Status.RewrittenByGame"] = "{0} a été réécrit par le jeu ; rechargé depuis le disque.",
        ["Status.ClientUnreadable"] =
            "Windows interdit à l'éditeur de lire {0} : ce client tourne avec plus de droits que cette "
            + "fenêtre. Clic droit sur l'éditeur puis « Exécuter en tant qu'administrateur » pour voir "
            + "le book ouvert — tout le reste fonctionne sans ça.",
        ["Status.SettingsNotSaved"] = "Réglages non enregistrés : {0}",
        ["Status.Copied"] = "{0} copié.",
        ["Status.Pasted"] = "Collé sur {0}.",
        ["Status.CopiedOnto"] = "{0} copié sur {1}.",
        ["Status.Swapped"] = "{0} et {1} échangés.",
        ["Status.NothingToPaste"] = "Rien à coller : copie d'abord une macro, un set ou un book (Ctrl+C).",
        ["Status.SetCopied"] = "Set {0} du book {1} copié — ses 20 macros.",
        ["Status.SetPasted"] = "Set {0} du book {1} remplacé. Enregistre pour appliquer.",
        ["Status.SetCleared"] = "Set {0} du book {1} vidé. Enregistre pour appliquer, ou recharge pour le récupérer.",
        ["Status.SetAlreadyEmpty"] = "Le set {0} du book {1} est déjà vide.",
        ["Status.BookRenamed"] = "Le book {0} s'appelle maintenant « {1} »." + "",
        ["Status.TitlesNotPushed"] = "Le client en cours n'a pas pris les nouveaux noms de {0}. Il risque de réécrire les siens par-dessus : reconnecte-toi, ou réorganise les books depuis l'écran de sélection de personnage.",
        ["Status.EverythingBackedUp"] = "{0} personnage(s), {1} fichier(s) sauvegardés vers {2}",
        ["Status.BookBackedUp"] = "Book {0} sauvegardé — {1} fichier(s) de set — vers {2}",
        ["Status.BookRestored"] = "Book {0} « {1} » restauré dans le book {2} : {3} set(s), les fichiers du jeu.",
        ["Status.BookTitleCleared"] = "Le book {0} reprend son nom d'origine, et le champ est nettoyé.",
        ["Status.CharacterRenamed"] =
            "{0} s'affiche désormais « {1} ». C'est une étiquette tenue par l'éditeur — le dossier sur "
            + "le disque garde son nom, et c'est celui-là que le jeu va chercher.",
        ["Status.CharacterUnnamed"] = "{0} s'affiche de nouveau sous le nom de son dossier.",
        ["Status.LiveUnmatched"] =
            "Windower signale {0} en jeu sur le book {1}, mais aucun personnage n'est étiqueté {0} ici. "
            + "Sélectionne ce personnage dans la liste, fais F2 et tape {0}. Rien n'est renommé sur le "
            + "disque : le jeu retrouve un personnage par le nom de son dossier, il ne doit pas changer.",
        ["Status.BookCleared"] = "Book {0} vidé : ses fichiers de set sont supprimés et son titre réinitialisé.",
        ["Status.NothingToClear"] = "Le book {0} est déjà vide.",
        ["Status.BookOnClipboard"] = "Book {0} « {1} » copié. Colle-le sur un autre book pour le remplacer.",
        ["Status.NothingToRepair"] = "{0} : rien à réparer.",
        ["Status.RepairedOne"] = "1 champ réparé ({0}). Enregistre pour appliquer.",
        ["Status.RepairedMany"] = "{0} champs réparés (dont {1}). Enregistre pour appliquer.",
        ["Status.ExportedOne"] = "Un set exporté vers {0}.",
        ["Status.Exported"] = "{1} sets exportés vers {0}.",
        ["Status.ImportedOne"] = "Un set importé depuis {0}. Enregistre pour appliquer.",
        ["Status.Imported"] = "{1} sets importés depuis {0}. Enregistre pour appliquer.",
        ["Status.Cancelled"] = "Opération annulée.",
        ["Status.BooksSwapped"] = "Books {0} et {1} échangés.",
        ["Status.BookCopied"] = "Book {0} copié vers le book {1}.",
        ["Status.LanguageChanged"] = "Langue de l'interface : français.",
        ["Status.CloseWithChanges"] = "{0}. Enregistre (Ctrl+S), ou referme la fenêtre pour quitter sans enregistrer.",

        // ---------------------------------------------------------------- le jeu tourne
        ["Game.Connected"] =
            "{0} est connecté en jeu. Le client ne détient que le book affiché à l'écran : celui-là "
            + "sera écrasé si tu l'enregistres. Tous les autres books se lisent sur le disque au "
            + "moment où tu bascules dessus, donc tu peux les modifier librement — la modification "
            + "sera active dès le changement de book ou de job.",
        ["Game.InGame"] = "En jeu : {0}",
        ["Game.NobodyConnected"] = "Personne n'est connecté en jeu : tu peux enregistrer.",
        ["Game.SaveAdvice"] = " Bascule sur le book {0} en jeu pour le voir.",

        // ---------------------------------------------------------------- déplacer un book
        ["Book.Copy"] = "Copier ce book",
        ["Book.Paste"] = "Coller un book ici",
        ["Book.Rename"] = "Renommer ce book (F2)",
        ["Character.Rename"] = "Étiqueter ce personnage dans l'éditeur (F2)",
        ["Tree.NotHexFolder"] =
            "le jeu ne trouvera pas ce dossier : il cherche un personnage par le nom hexadécimal qu'il "
            + "lui a donné, un dossier renommé lui est donc invisible",
        ["Book.Clear"] = "Vider ce book",
        ["Book.OpenInGameWarning"] =
            "Attention : le book {0} « {1} » est celui ouvert en jeu en ce moment. Le client le détient et "
            + "réécrit sa propre copie en le quittant, ce qui annulerait l'opération. Change de book en jeu "
            + "d'abord, puis recommence. — ",
        ["Book.ClearQuestion"] =
            "Vider le book {0} « {1} » de {2} ? Ses {3} fichier(s) de set seront supprimés et son titre "
            + "réinitialisé. Rien n'est gardé dans l'éditeur — seule la sauvegarde peut le ramener.",
        ["Book.SwapQuestion"] = "Échanger le book {0} « {1} » de {2} avec le book {3} « {4} » de {5} ? Ils prennent la place l'un de l'autre — macros et noms — et rien n'est perdu.",
        ["Book.CopyQuestion"] = "Copier le book {0} « {1} » de {2} vers le book {3} « {4} » de {5} ? ",
        ["Book.Overwrites"] = "{0} set(s) du book de destination seront écrasés",
        ["Book.End"] = ".",
        ["Book.NeedsSave"] =
            " Avant ça : {0} set(s) ont des modifications non enregistrées. Copier un book relit tous "
            + "les fichiers depuis le disque, ce qui les perdrait — elles seront donc enregistrées au passage.",
    };
}
