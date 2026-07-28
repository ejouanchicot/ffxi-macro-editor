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
        ["Books.ShowEmpty"] = "empty",
        ["Books.ShowEmpty.Tip"] = "Show all 40 books, including the ones with no macro",
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
        ["Set.Export"] = "Export",
        ["Set.Export.Tip"] = "Export this set as .txt or .json",
        ["Set.Import"] = "Import",
        ["Set.Import.Tip"] = "Import a .txt or .json into this set",
        ["Set.Title"] = "{0}   ·   Book {1} “{2}”   ·   Set {3}/10",
        ["Set.PickBook"] = "Pick a book from the list on the left.",
        ["Set.Wheel.Tip"] = "The order the game's up and down arrows walk. Ctrl+PageUp / Ctrl+PageDown.",

        // ---------------------------------------------------------------- macro editor
        ["Editor.Clear"] = "Clear",
        ["Editor.NameWatermark"] = "Name (8 characters)",
        ["Editor.Copy"] = "Copy",
        ["Editor.Paste"] = "Paste",

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
        ["Notice.RecheckGame"] = "Refresh game state",
        ["Notice.Confirm"] = "Confirm",
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
        ["Status.SettingsNotSaved"] = "Settings not saved: {0}",
        ["Status.Copied"] = "{0} copied.",
        ["Status.Pasted"] = "Pasted onto {0}.",
        ["Status.CopiedOnto"] = "{0} copied onto {1}.",
        ["Status.Swapped"] = "{0} and {1} swapped.",
        ["Status.NothingToRepair"] = "{0}: nothing to repair.",
        ["Status.RepairedOne"] = "1 field repaired ({0}). Save to apply.",
        ["Status.RepairedMany"] = "{0} fields repaired (including {1}). Save to apply.",
        ["Status.ExportedOne"] = "One set exported to {0}.",
        ["Status.Exported"] = "{1} sets exported to {0}.",
        ["Status.ImportedOne"] = "One set imported from {0}. Save to apply.",
        ["Status.Imported"] = "{1} sets imported from {0}. Save to apply.",
        ["Status.Cancelled"] = "Operation cancelled.",
        ["Status.SaveBeforeMove"] = "{0}: save before moving a book.",
        ["Status.BookMoved"] = "Book {0} moved onto book {1}.",
        ["Status.BookCopied"] = "Book {0} copied onto book {1}.",
        ["Status.LanguageChanged"] = "Interface language: English.",
        ["Status.CloseWithChanges"] = "{0}. Save (Ctrl+S), or close the window again to quit without saving.",

        // ---------------------------------------------------------------- the game is running
        ["Game.Connected"] =
            "{0} is in game. The client only holds the book shown on screen: that one will be "
            + "overwritten if you save it. Every other book is read from disk the moment you switch "
            + "to it, so you can edit those freely — the change takes effect as soon as you change "
            + "book or job.",
        ["Game.NobodyConnected"] = "Nobody is in game: you can save.",
        ["Game.SaveAdvice"] = " Switch to book {0} in game to see it — if you are already there, change book and come back.",

        // ---------------------------------------------------------------- moving a book
        ["Book.MoveQuestion"] = "Move book {0} “{1}” from {2} onto book {3} “{4}” of {5}? ",
        ["Book.CopyQuestion"] = "Copy book {0} “{1}” from {2} onto book {3} “{4}” of {5}? ",
        ["Book.Overwrites"] = "{0} set(s) of the destination book will be overwritten",
        ["Book.SourceEmptied"] = ", and the source book will be emptied.",
        ["Book.End"] = ".",
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
        ["Books.ShowEmpty"] = "vides",
        ["Books.ShowEmpty.Tip"] = "Afficher les 40 books, même ceux sans macro",
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
        ["Set.Export"] = "Exporter",
        ["Set.Export.Tip"] = "Exporter ce set en .txt ou .json",
        ["Set.Import"] = "Importer",
        ["Set.Import.Tip"] = "Importer un .txt ou .json dans ce set",
        ["Set.Title"] = "{0}   ·   Book {1} « {2} »   ·   Set {3}/10",
        ["Set.PickBook"] = "Choisis un book dans la liste de gauche.",
        ["Set.Wheel.Tip"] = "Ordre des flèches haut/bas du jeu. Ctrl+PagePréc / Ctrl+PageSuiv.",

        // ---------------------------------------------------------------- éditeur de macro
        ["Editor.Clear"] = "Vider",
        ["Editor.NameWatermark"] = "Nom (8 caractères)",
        ["Editor.Copy"] = "Copier",
        ["Editor.Paste"] = "Coller",

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
        ["Notice.RecheckGame"] = "Actualiser l'état du jeu",
        ["Notice.Confirm"] = "Confirmer",
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
        ["Status.SettingsNotSaved"] = "Réglages non enregistrés : {0}",
        ["Status.Copied"] = "{0} copié.",
        ["Status.Pasted"] = "Collé sur {0}.",
        ["Status.CopiedOnto"] = "{0} copié sur {1}.",
        ["Status.Swapped"] = "{0} et {1} échangés.",
        ["Status.NothingToRepair"] = "{0} : rien à réparer.",
        ["Status.RepairedOne"] = "1 champ réparé ({0}). Enregistre pour appliquer.",
        ["Status.RepairedMany"] = "{0} champs réparés (dont {1}). Enregistre pour appliquer.",
        ["Status.ExportedOne"] = "Un set exporté vers {0}.",
        ["Status.Exported"] = "{1} sets exportés vers {0}.",
        ["Status.ImportedOne"] = "Un set importé depuis {0}. Enregistre pour appliquer.",
        ["Status.Imported"] = "{1} sets importés depuis {0}. Enregistre pour appliquer.",
        ["Status.Cancelled"] = "Opération annulée.",
        ["Status.SaveBeforeMove"] = "{0} : enregistre avant de déplacer un book.",
        ["Status.BookMoved"] = "Book {0} déplacé vers le book {1}.",
        ["Status.BookCopied"] = "Book {0} copié vers le book {1}.",
        ["Status.LanguageChanged"] = "Langue de l'interface : français.",
        ["Status.CloseWithChanges"] = "{0}. Enregistre (Ctrl+S), ou referme la fenêtre pour quitter sans enregistrer.",

        // ---------------------------------------------------------------- le jeu tourne
        ["Game.Connected"] =
            "{0} est connecté en jeu. Le client ne détient que le book affiché à l'écran : celui-là "
            + "sera écrasé si tu l'enregistres. Tous les autres books se lisent sur le disque au "
            + "moment où tu bascules dessus, donc tu peux les modifier librement — la modification "
            + "sera active dès le changement de book ou de job.",
        ["Game.NobodyConnected"] = "Personne n'est connecté en jeu : tu peux enregistrer.",
        ["Game.SaveAdvice"] = " Bascule sur le book {0} en jeu pour le voir — si tu y es déjà, change de book et reviens.",

        // ---------------------------------------------------------------- déplacer un book
        ["Book.MoveQuestion"] = "Déplacer le book {0} « {1} » de {2} vers le book {3} « {4} » de {5} ? ",
        ["Book.CopyQuestion"] = "Copier le book {0} « {1} » de {2} vers le book {3} « {4} » de {5} ? ",
        ["Book.Overwrites"] = "{0} set(s) du book de destination seront écrasés",
        ["Book.SourceEmptied"] = ", et le book d'origine sera vidé.",
        ["Book.End"] = ".",
    };
}
