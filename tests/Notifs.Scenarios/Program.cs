using System.Windows;
using System.IO;
using System.Windows.Documents;
using Notifs.App;
using Notifs.Core;

namespace Notifs.Scenarios;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--typing-target"))
        {
            var targetApp = new Application();
            var input = new System.Windows.Controls.TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 20, Margin = new Thickness(20), Text = "Проверка ввода: " };
            System.Windows.Automation.AutomationProperties.SetName(input, "Проверка печати при уведомлениях");
            var target = new Window { Title = "Notifs QA — проверка печати", Width = 600, Height = 240, Left = 100, Top = 100, Content = input };
            target.Loaded += (_, _) => { input.Focus(); input.CaretIndex = input.Text.Length; };
            return targetApp.Run(target);
        }
        if (args.Contains("--manual-notifications")) return ManualNotifications.Run();
        var directory = Path.Combine(Path.GetTempPath(), "Notifs.Scenarios", Guid.NewGuid().ToString("N"));
        try
        {
            DraftScenarios.Run(directory);
            if (args.Contains("--drafts")) return 0;
            NotificationScenarios.Run(directory);
            if (args.Contains("--notifications")) return 0;
            FieldScenarios.Run(directory);
            if (args.Contains("--fields")) return 0;
            var clock = new ManualClock(new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
            var path = Path.Combine(directory, "tasks.db");
            var tasks = new TaskService(path, clock);
            tasks.Initialize();
            Check(tasks.List().Count == 0 && tasks.List(true).Count == 0, "clean database starts empty");
            const string markdown = "# План\n\n- **Купить** продукты\n- *Отдохнуть*\n\n```csharp\nvar x = 1;\n```\n\n![offline](https://invalid.example/image.png)\n<script>not executable</script>";
            try { tasks.Create(" \t\n", markdown); throw new Exception("blank title accepted"); }
            catch (ArgumentException) { }
            Check(tasks.List().Count == 0, "invalid create makes no record");
            var task = tasks.Create("  Подготовить ужин  ", markdown);
            Check(task.Title == "Подготовить ужин" && task.CreatedAt == clock.GetUtcNow(), "creation trims title and uses supplied clock");
            clock.Advance(TimeSpan.FromDays(3));
            try { tasks.Edit(task.Id, " ", "lost"); throw new Exception("invalid edit accepted"); }
            catch (ArgumentException) { }
            Check(tasks.List().Single().Description == markdown, "invalid edit preserves original record");
            tasks.Edit(task.Id, "Ужин на двоих ' ; --", markdown + "\nГотовим вместе");
            var reopened = new TaskService(path, clock);
            reopened.Initialize();
            var edited = reopened.List().Single();
            Check(edited.CreatedAt == task.CreatedAt && edited.Description.EndsWith("Готовим вместе"), "edit persists across connection reopen without changing creation time");
            Check(edited.Title == "Ужин на двоих ' ; --", "title safely round-trips SQL punctuation");
            reopened.Complete(task.Id);
            var completed = new TaskService(path, clock).List(true).Single();
            Check(reopened.List().Count == 0 && completed.CompletedAt == clock.GetUtcNow(), "completion moves task to persistent history");
            clock.Advance(TimeSpan.FromDays(1));
            reopened.Complete(task.Id);
            Check(reopened.List(true).Single().CompletedAt == completed.CompletedAt, "repeated completion preserves completion timestamp");
            reopened.Edit(task.Id, "Завершённый ужин", markdown);
            Check(reopened.List(true).Single().CreatedAt == task.CreatedAt, "editing history preserves creation time and state");
            reopened.Restore(task.Id);
            var restored = new TaskService(path, clock).List().Single();
            Check(restored.CompletedAt is null && restored.CreatedAt == task.CreatedAt && reopened.List(true).Count == 0, "restore persists original task and creation time");
            try { reopened.Restore(Guid.NewGuid()); throw new Exception("missing id accepted"); }
            catch (KeyNotFoundException) { }
            var later = reopened.Create("Следующее дело", "");
            Check(reopened.List().Select(x => x.Id).SequenceEqual(new[] { task.Id, later.Id }), "active tasks ordered oldest first");

            clock.Advance(TimeSpan.FromMinutes(1));
            reopened.Complete(later.Id);
            clock.Advance(TimeSpan.FromMinutes(1));
            reopened.Complete(task.Id);
            Check(reopened.ListAll().Select(x => x.Id).SequenceEqual(new[] { task.Id, later.Id }), "all tasks sort completed by recent completion");
            reopened.Restore(later.Id);
            Check(reopened.ListAll().Select(x => x.Id).SequenceEqual(new[] { later.Id, task.Id }), "all tasks keep incomplete above completed");
            reopened.Initialize();
            Check(reopened.ListAll().Count == 2 && reopened.ListAll().Single(x => x.Id == task.Id).CreatedAt == task.CreatedAt, "schema initialization preserves previous identities and data");

            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Notifs.App;component/Theme.xaml", UriKind.Relative) });
            var view = new MarkdownView { Source = markdown };
            var blocks = view.Document.Blocks.ToArray();
            Check(blocks.OfType<Paragraph>().Any(p => p.FontSize == 26), "Markdown heading is formatted natively");
            Check(blocks.OfType<System.Windows.Documents.List>().Single().ListItems.Count == 2, "Markdown list becomes native list");
            var listText = blocks.OfType<System.Windows.Documents.List>().Single().ListItems.FirstListItem.Blocks.OfType<Paragraph>().Single();
            Check(listText.Inlines.OfType<Bold>().Any(), "Markdown emphasis becomes bold text");
            Check(blocks.OfType<Paragraph>().Any(p => p.FontFamily.Source == "Consolas"), "code block uses monospace text");
            var text = new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text;
            Check(text.Contains("var x = 1;") && text.Contains("[изображение]") && text.Contains("not executable"), "code retained; remote image and raw HTML stay inert text");
            var editor = new EditorViewModel(reopened, null) { Description = markdown };
            editor.SaveCommand.Execute(null); // Invalid title is synchronous, no dispatcher pump needed.
            Check(editor.HasError && editor.Description == markdown && reopened.ListAll().Count == 2, "invalid UI submission retains Markdown and creates nothing");
            Console.WriteLine("All application and rendering checks passed.");
            return UiScenario.Run(app, Path.Combine(directory, "ui.db"), args.Contains("--fields-ui"), args.Contains("--drafts-ui"), args.Contains("--notifications-ui"));
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
