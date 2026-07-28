using System.Collections.ObjectModel;
using FfxiMacros.Core.Text;

using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _phraseQuery = "";
    private bool _phrasePanelOpen;
    private bool _phraseIncludeItems;

    /// <summary>
    /// Set by the view: writes the chosen escape into the line being edited, at the caret.
    /// Returns false when no line has the focus.
    /// </summary>
    public Func<string, bool>? InsertIntoFocusedField { get; set; }

    public bool PhrasePanelOpen
    {
        get => _phrasePanelOpen;
        set => SetField(ref _phrasePanelOpen, value);
    }

    /// <summary>What the user has typed so far; the list narrows on every keystroke.</summary>
    public string PhraseQuery
    {
        get => _phraseQuery;
        set
        {
            if (SetField(ref _phraseQuery, value))
                RunPhraseSearch();
        }
    }

    /// <summary>Items are 23 000 entries, so they are off by default and searched on demand.</summary>
    public bool PhraseIncludeItems
    {
        get => _phraseIncludeItems;
        set
        {
            if (SetField(ref _phraseIncludeItems, value))
                RunPhraseSearch();
        }
    }

    public ObservableCollection<AutoTranslateDictionary.Phrase> PhraseResults { get; } = [];

    public string PhraseSummary { get; private set; } = "";

    public bool HasPhraseDictionary => !FfxiText.DefaultAutoTranslate.IsEmpty;

    private RelayCommand? _togglePhrasesCommand;
    public RelayCommand TogglePhrasesCommand =>
        _togglePhrasesCommand ??= new RelayCommand(() =>
        {
            PhrasePanelOpen = !PhrasePanelOpen;
            if (PhrasePanelOpen && !HasPhraseDictionary)
                SetStatus(Loc.T("Phrases.NoDictionary"), error: true);
        });

    private RelayCommand<AutoTranslateDictionary.Phrase>? _insertPhraseCommand;

    /// <summary>Drops the phrase into the line being edited.</summary>
    public RelayCommand<AutoTranslateDictionary.Phrase> InsertPhraseCommand =>
        _insertPhraseCommand ??= new RelayCommand<AutoTranslateDictionary.Phrase>(match =>
        {
            if (InsertIntoFocusedField?.Invoke(match.Escape) == true)
                SetStatus(Loc.T("Phrases.Inserted", match.Name));
            else
                SetStatus(Loc.T("Phrases.ClickFirst"), error: true);
        });

    private void RunPhraseSearch()
    {
        PhraseResults.Clear();

        var found = FfxiText.DefaultAutoTranslate.Search(_phraseQuery, includeItems: _phraseIncludeItems);
        foreach (var match in found)
            PhraseResults.Add(match);

        PhraseSummary = _phraseQuery.Trim().Length == 0
            ? Loc.T("Phrases.Known", FfxiText.DefaultAutoTranslate.Count)
            : found.Count switch
            {
                0 => Loc.T("Phrases.NoneFor", _phraseQuery),
                1 => Loc.T("Phrases.One"),
                _ => Loc.T("Phrases.Many", found.Count),
            };

        OnPropertyChanged(nameof(PhraseSummary));
    }
}
