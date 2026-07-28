using FfxiMacros.App.ViewModels;
using FfxiMacros.Core.Settings;
using Xunit;

namespace FfxiMacros.Tests;

/// <summary>
/// Whether the buttons under a set are actually clickable.
/// </summary>
/// <remarks>
/// Repair, Export and Import shipped permanently greyed out: nothing ever re-asked them whether they
/// could run, and the one place that asks the others did so before the set had been read, when the
/// answer was still no. Every command on that row is checked here, so adding one and forgetting to
/// wire it up fails a test instead of reaching a user.
/// </remarks>
public class SetActionAvailabilityTests : IDisposable
{
    private readonly TempUserFolder _temp = new();
    private readonly EditorSettings _settings;

    public SetActionAvailabilityTests()
    {
        _temp.AddCharacter("a1b2c3d", 0, 1, 2);
        _settings = new EditorSettings { UserFolder = _temp.UserFolder, BackupBeforeSave = false };
    }

    private MainWindowViewModel NewViewModel()
    {
        var viewModel = new MainWindowViewModel(_settings) { ProbeRunningClients = () => [] };
        viewModel.Initialize();
        return viewModel;
    }

    [Fact]
    public void TheSetActionsAreOfferedOnceASetIsOpen()
    {
        var viewModel = NewViewModel();

        Assert.NotNull(viewModel.CurrentSet);
        Assert.True(viewModel.CurrentSet!.IsLoaded);

        Assert.True(viewModel.RepairCommand.CanExecute(null), "Repair is greyed out");
        Assert.True(viewModel.ExportSetCommand.CanExecute(null), "Export is greyed out");
        Assert.True(viewModel.ImportSetCommand.CanExecute(null), "Import is greyed out");
    }

    [Fact]
    public void TheyStayOfferedAfterSwitchingSets()
    {
        var viewModel = NewViewModel();

        viewModel.SelectSetCommand.Execute(viewModel.CurrentSets[1]);

        Assert.True(viewModel.RepairCommand.CanExecute(null));
        Assert.True(viewModel.ExportSetCommand.CanExecute(null));
        Assert.True(viewModel.ImportSetCommand.CanExecute(null));
    }

    [Fact]
    public void TheyAreWithdrawnWhenNoSetIsOpen()
    {
        var viewModel = NewViewModel();

        viewModel.CurrentSet = null;

        Assert.False(viewModel.RepairCommand.CanExecute(null));
        Assert.False(viewModel.ExportSetCommand.CanExecute(null));
        Assert.False(viewModel.ImportSetCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingASetTellsTheButtonsToLookAgain()
    {
        // The bug was silent because CanExecute was right and simply never re-read. What matters is
        // that the event fires, which is the only thing a button listens to.
        var viewModel = NewViewModel();
        viewModel.CurrentSet = null;

        int repair = 0, export = 0, import = 0;
        viewModel.RepairCommand.CanExecuteChanged += (_, _) => repair++;
        viewModel.ExportSetCommand.CanExecuteChanged += (_, _) => export++;
        viewModel.ImportSetCommand.CanExecuteChanged += (_, _) => import++;

        viewModel.SelectSetCommand.Execute(viewModel.CurrentSets[0]);

        Assert.True(repair > 0, "Repair was never asked again");
        Assert.True(export > 0, "Export was never asked again");
        Assert.True(import > 0, "Import was never asked again");
    }

    [Fact]
    public void ExportAndImportRunEndToEnd()
    {
        // Enabled is not the same as working: the commands are actually executed here, through the
        // same file callbacks the window supplies.
        var viewModel = NewViewModel();
        string path = Path.Combine(_temp.UserFolder, "exported.txt");

        viewModel.CurrentSet!.Macros[0].Name = "Probe";
        viewModel.SaveFileAsync = (_, _) => Task.FromResult<string?>(path);
        viewModel.ExportSetCommand.Execute(null);

        Assert.True(File.Exists(path), "Export wrote nothing");
        Assert.Contains("Probe", File.ReadAllText(path), StringComparison.Ordinal);

        viewModel.CurrentSet.Macros[0].Name = "Other";
        viewModel.OpenFileAsync = _ => Task.FromResult<string?>(path);
        viewModel.ImportSetCommand.Execute(null);

        Assert.Equal("Probe", viewModel.CurrentSet.Macros[0].Name);
    }

    [Fact]
    public void RepairRunsAndSaysWhatItFixed()
    {
        // The sample this character is built from carries the damage of a real one: fields whose
        // leading slash was replaced by a null byte, which the game silently ignores.
        var viewModel = NewViewModel();

        viewModel.RepairCommand.Execute(null);

        Assert.Contains("repaired", viewModel.Status, StringComparison.Ordinal);
        Assert.Contains("line", viewModel.Status, StringComparison.Ordinal);
        Assert.False(viewModel.StatusIsError);
        Assert.True(viewModel.CurrentSet!.IsDirty, "a repair that changed nothing is not a repair");
    }

    [Fact]
    public void ASecondRepairHasNothingLeftToDo()
    {
        var viewModel = NewViewModel();
        viewModel.RepairCommand.Execute(null);

        viewModel.RepairCommand.Execute(null);

        Assert.Contains("nothing to repair", viewModel.Status, StringComparison.Ordinal);
    }

    public void Dispose() => _temp.Dispose();
}
