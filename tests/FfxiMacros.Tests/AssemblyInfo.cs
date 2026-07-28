using Xunit;

// The auto-translate tests swap FfxiText.DefaultAutoTranslate, which every decode reads.
// The whole suite runs in well under a second, so serialising it costs nothing and removes
// any chance of one test seeing another's dictionary.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
