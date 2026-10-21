using System.Globalization;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Dialogs;

/// The "Clean up unused files" dialog: the files nothing in layout.yaml names (UnusedFiles), each with its size and a
/// tick, all ticked at first, and the ones ticked when the user confirms (Chosen).
public sealed class CleanUpViewModel : ObservableObject
{
    public CleanUpViewModel(IReadOnlyList<UnusedFile> files)
    {
        Files = files.Select(file => new CleanUpFile(file, this)).ToList();
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
    }

    public IReadOnlyList<CleanUpFile> Files { get; }

    /// The ticked files' paths, as UnusedFiles.Delete takes them.
    public IReadOnlyList<string> Chosen => Files.Where(file => file.IsChecked).Select(file => file.Path).ToList();

    public bool CanDelete => Files.Any(file => file.IsChecked);

    /// The delete button: "Delete 3 files (1.2 MB)".
    public string DeleteText
    {
        get
        {
            var chosen = Files.Where(file => file.IsChecked).ToList();
            return Strings.DeleteFiles(chosen.Count.ToString(CultureInfo.CurrentCulture), Size(chosen.Sum(file => file.Bytes)));
        }
    }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    /// A size as Explorer shows one: bytes, KB, MB or GB.
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => Strings.SizeBytes(bytes.ToString(CultureInfo.CurrentCulture)),
        < 1024 * 1024 => Strings.SizeKilobytes((bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture)),
        < 1024L * 1024 * 1024 => Strings.SizeMegabytes((bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture)),
        _ => Strings.SizeGigabytes((bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture)),
    };

    internal void TickChanged()
    {
        Raise(nameof(Chosen));
        Raise(nameof(CanDelete));
        Raise(nameof(DeleteText));
    }

    void SetAll(bool on)
    {
        foreach (var file in Files) file.IsChecked = on;
    }
}

/// One file in the clean-up list.
public sealed class CleanUpFile(UnusedFile file, CleanUpViewModel dialog) : ObservableObject
{
    bool _isChecked = true;

    /// Relative to the theme folder, with forward slashes.
    public string Path => file.Path;

    public long Bytes => file.Bytes;

    public string SizeText => CleanUpViewModel.Size(file.Bytes);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value)) dialog.TickChanged();
        }
    }
}
