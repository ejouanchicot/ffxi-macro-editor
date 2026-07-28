using FfxiMacros.App.Localization;

namespace FfxiMacros.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public bool IsEnglish => Loc.Current == Language.English;

    public bool IsFrench => Loc.Current == Language.French;

    private RelayCommand<string>? _setLanguageCommand;

    /// <summary>Switches the interface language and remembers the choice.</summary>
    public RelayCommand<string> SetLanguageCommand =>
        _setLanguageCommand ??= new RelayCommand<string>(code =>
        {
            var language = Loc.Parse(code);
            if (language == Loc.Current)
                return;

            Loc.Apply(language);
            _settings.Language = Loc.Code;
            TrySaveSettings();

            RefreshLocalizedText();
            SetStatus(Loc.T("Status.LanguageChanged"));
        });

    /// <summary>
    /// Re-raises everything whose text this class composed itself.
    /// </summary>
    /// <remarks>
    /// Labels written in XAML follow the swapped resource dictionary on their own. These do not:
    /// they were built into strings once, so every one of them has to be asked for again — down to
    /// each node of the tree, which numbers its own sets and books.
    /// </remarks>
    private void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(IsFrench));
        OnPropertyChanged(nameof(UserFolder));
        OnPropertyChanged(nameof(DirtySummary));
        OnPropertyChanged(nameof(CurrentSetTitle));
        OnPropertyChanged(nameof(GameRunningWarning));
        OnPropertyChanged(nameof(PendingBookOperation));

        foreach (var character in Characters.OfType<CharacterNodeViewModel>())
        {
            character.RefreshLabels();
            foreach (var book in character.Books)
            {
                book.RefreshLabels();
                foreach (var set in book.Sets)
                    set.RefreshLabels();
            }
        }

        // Re-worded from the counts already on screen: re-running the search would walk every file
        // again, and the phrase list is rebuilt from a dictionary already in memory.
        if (SearchPanelOpen)
        {
            SearchSummary = SearchSummaryFor(SearchResults.Count);
            OnPropertyChanged(nameof(SearchSummary));
        }

        if (PhrasePanelOpen)
            RunPhraseSearch();
    }
}
