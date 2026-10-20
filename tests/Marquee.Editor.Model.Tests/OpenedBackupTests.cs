using Marquee.Editor.Model.Tests.TestSupport;

namespace Marquee.Editor.Model.Tests;

/// "As it was when you opened it": the file's bytes at open, kept under the editor's root before the first save, and
/// put back as one undo step.
public class OpenedBackupTests
{
    [Fact]
    public void Revert_puts_back_the_opened_text_as_one_undo_step()
    {
        using var saving = new Saving();
        saving.Move(20);
        saving.Advance(1);
        saving.Move(30);

        saving.Session.Apply(saving.Backup.Revert());

        Assert.Equal(Saving.Layout, saving.Session.Document.Text);
        Assert.Equal(OpenedBackup.RevertLabel, saving.Session.History.NextUndo!.Label);
        saving.Advance(1);
        Assert.Equal(Saving.Layout, saving.OnDisk);

        saving.Session.Undo();
        Assert.Equal(Saving.Moved(30), saving.Session.Document.Text);
    }

    [Fact]
    public void Revert_keeps_a_byte_order_mark()
    {
        using var theme = TempTheme.With("﻿" + Saving.Layout);
        using var saving = new Saving(theme);
        saving.Move(20);

        saving.Session.Apply(saving.Backup.Revert());

        Assert.Equal("﻿" + Saving.Layout, saving.Session.Document.Text);
    }

    [Fact]
    public void A_new_session_keeps_its_own_opened_version()
    {
        using var data = new TempFolder();
        using var theme = TempTheme.With(Saving.Layout);
        using (var first = new Saving(theme, data))
        {
            first.Move(20);
            first.Advance(1);
        }

        using var second = new Saving(theme, data);
        Assert.Equal(Saving.Layout, File.ReadAllText(second.Backup.Path));

        second.Move(30);
        second.Advance(1);

        Assert.Equal(Saving.Moved(20), File.ReadAllText(second.Backup.Path));
        second.Session.Apply(second.Backup.Revert());
        Assert.Equal(Saving.Moved(20), second.Session.Document.Text);
    }
}
