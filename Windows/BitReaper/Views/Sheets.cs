using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>Modal cards shown over the results: the delete review and the list of failures.</summary>
public static class Sheets
{
    public static FrameworkElement Delete(AppModel model, ScanSession session, Action close)
    {
        var collector = session.Collector;
        var mode = model.Prefs.DeleteMode;

        var reaper = Ui.Image("ReaperFolder", width: 70);
        var title = Ui.Display($"Delete {Ui.Plural(collector.Count, "collected item")}?", 22, bold: true);
        var total = Ui.Text($"Total: {ByteFormat.Size(collector.TotalSize)}", 13, Theme.TextSecondary);
        var head = Ui.Row(14, reaper, Ui.Column(3, title, total));

        var list = new ListBox { Style = Ui.Res<Style>("SheetList"), Height = 220, Focusable = false };
        foreach (var item in collector.SortedItems)
        {
            var size = Ui.Text(ByteFormat.Size(item.Node.Size), 13, Theme.TextSecondary);
            size.Margin = new Thickness(10, 0, 0, 0);
            var icon = Ui.Icon(IconCache.Icon(item.Node), 18);
            icon.Margin = new Thickness(0, 0, 8, 0);
            var dock = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(size, Dock.Right);
            dock.Children.Add(icon);
            dock.Children.Add(size);
            dock.Children.Add(Ui.Column(0, Ui.Text(item.Node.Name, 13), Ui.Text(Ui.Abbreviate(item.Path), 11, Theme.TextSecondary)));
            list.Items.Add(new ListBoxItem { Content = dock, Focusable = false });
        }
        var listPanel = Ui.Panel(list, new Thickness(4), corner: 10);

        var description = Ui.Text("", 13, wrap: true);
        var confirm = Ui.Button("", Ui.Kind.Danger);
        confirm.IsDefault = true;
        var recycle = new RadioButton { Style = Ui.Res<Style>("NeonRadio"), Content = DeleteMode.RecycleBin.Title(), GroupName = "deleteMode" };
        var permanent = new RadioButton { Style = Ui.Res<Style>("NeonRadio"), Content = DeleteMode.Permanent.Title(), GroupName = "deleteMode", Margin = new Thickness(22, 0, 0, 0) };
        void Update()
        {
            description.Text = mode == DeleteMode.RecycleBin
                ? "Items go to the Recycle Bin. You can put them back right after; the space is reclaimed when the Recycle Bin is emptied. " +
                  "Windows asks first if an item is too big for the Recycle Bin."
                : "Items are removed permanently and the space is reclaimed right away. This can't be undone.";
            description.Foreground = Theme.Brush(mode == DeleteMode.Permanent ? Theme.Danger : Theme.TextSecondary);
            confirm.Content = mode == DeleteMode.RecycleBin ? "Move to Recycle Bin" : "Delete Permanently";
            reaper.Effect = null;
            reaper.WithGlow(mode == DeleteMode.RecycleBin ? Theme.Magenta : Theme.Danger, 8);
        }
        recycle.IsChecked = mode == DeleteMode.RecycleBin;
        permanent.IsChecked = mode == DeleteMode.Permanent;
        recycle.Checked += (_, _) => { mode = DeleteMode.RecycleBin; Update(); };
        permanent.Checked += (_, _) => { mode = DeleteMode.Permanent; Update(); };
        Update();

        var column = Ui.Column(16, head, listPanel, Ui.Row(0, recycle, permanent), description);
        if (model.Prefs.CountdownSeconds > 0)
        {
            var note = Ui.Text($"You'll have {model.Prefs.CountdownSeconds} seconds to cancel after confirming.", 12, Theme.TextSecondary);
            note.Margin = new Thickness(0, -8, 0, 0);
            column.Children.Add(note);
        }

        var dontAsk = new CheckBox { Style = Ui.Res<Style>("NeonCheckBox"), Content = "Don't ask again", IsChecked = !model.Prefs.ConfirmDelete };
        dontAsk.Checked += (_, _) => model.Prefs.ConfirmDelete = false;
        dontAsk.Unchecked += (_, _) => model.Prefs.ConfirmDelete = true;
        var cancel = Ui.Button("Cancel", click: close);
        cancel.IsCancel = true;
        confirm.Click += (_, _) => session.ConfirmDelete(mode);
        column.Children.Add(Ui.Spread(dontAsk, Ui.Row(10, cancel, confirm)));

        var card = Ui.Panel(column, new Thickness(24), glow: Theme.Magenta, fill: Background());
        card.Width = 600;
        return card;
    }

    public static FrameworkElement Failures(List<DeletionFailure> failures, Action dismiss)
    {
        var title = Ui.Row(8, Ui.Glyph(Ui.GlyphWarning, 18, Theme.Orange),
                           Ui.Display($"{Ui.Plural(failures.Count, "item")} couldn't be deleted", 18, Theme.Orange));
        var list = new ListBox { Style = Ui.Res<Style>("SheetList"), Height = 200 };
        foreach (var failure in failures)
        {
            var path = new TextBox
            {
                Text = Ui.Abbreviate(failure.Path), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Theme.Brush(Theme.Text), TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0),
            };
            list.Items.Add(new ListBoxItem { Content = Ui.Column(2, path, Ui.Text(failure.Reason, 11, Theme.TextSecondary, wrap: true)) });
        }
        var note = Ui.Secondary("They are still in the Collector. Items protected by Windows can't be removed; others may be open in " +
                                "another program or need BitReaper to run as administrator.", 13);
        var ok = Ui.Button("OK", Ui.Kind.Primary, click: dismiss);
        ok.IsDefault = true;
        ok.MinWidth = 80;
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        var card = Ui.Panel(Ui.Column(14, title, Ui.Panel(list, new Thickness(4), corner: 10), note, ok),
                            new Thickness(22), glow: Theme.Orange, fill: Background());
        card.Width = 560;
        return card;
    }

    private static Brush Background() =>
        new LinearGradientBrush(Theme.Night.WithAlpha(0.98), Theme.NightDeep.WithAlpha(0.98), 90);
}
