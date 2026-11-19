using Notifs.Core;

namespace Notifs.Scenarios;

internal static class DraftScenarios
{
    public static void Run(string directory)
    {
        var path = System.IO.Path.Combine(directory, "drafts.db");
        var service = new TaskService(path);
        service.Initialize();
        var a = service.Create("A", "сохранено");
        var b = service.Create("B", "сохранено B");
        var draft = new TaskDraft("черновик A", "несохранено", "не дата", "", "", "0", ImportanceRank.S);
        service.SaveDraft(a.Id, draft);
        service.SaveDraft(b.Id, draft with { Title = "черновик B" });
        service.SaveDraft(null, draft with { Title = "новая" });
        var reopened = new TaskService(path);
        reopened.Initialize();
        Check(reopened.ReadDraft(a.Id) == draft && reopened.ReadDraft(b.Id)?.Title == "черновик B" && reopened.ReadDraft(null)?.Title == "новая", "three independent drafts survive reopening, including invalid fields");
        Check(reopened.ListAll().Single(x => x.Id == a.Id) == a, "draft does not change saved task");
        reopened.DiscardDraft(b.Id);
        Check(reopened.ReadDraft(b.Id) is null && reopened.ReadDraft(a.Id) == draft && reopened.ReadDraft(null) is not null, "discard clears only selected draft");
        reopened.Complete(a.Id);
        var completed = reopened.List(true).Single();
        reopened.DeleteTask(a.Id);
        Check(reopened.ListAll().Count == 1 && reopened.ReadDraft(a.Id) is null, "delete removes saved task and its draft together");
        reopened = new TaskService(path);
        Check(reopened.DeletedTitle == "A", "last deletion survives restart");
        var restored = reopened.RestoreDeleted();
        Check(restored == completed && reopened.ReadDraft(a.Id) == draft && reopened.DeletedTitle is null, "restore preserves identity, completion and draft and consumes slot");
        reopened.DeleteTask(a.Id);
        reopened.DeleteTask(b.Id);
        Check(reopened.RestoreDeleted() == b && reopened.ListAll().Count == 1, "delete A then B restores only B");
        try { reopened.RestoreDeleted(); throw new Exception("restore consumed slot accepted twice"); }
        catch (KeyNotFoundException) { }
        reopened.SaveDraft(b.Id, draft);
        var c = reopened.Create("C", "для проверки ошибки");
        reopened.DeleteTask(c.Id);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_delete BEFORE DELETE ON tasks BEGIN SELECT RAISE(ABORT,'write blocked'); END;";
            command.ExecuteNonQuery();
        }
        try { reopened.DeleteTask(b.Id); throw new Exception("failed deletion reported success"); }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
        Check(reopened.ListAll().Single() == b && reopened.ReadDraft(b.Id) == draft && reopened.DeletedTitle == "C", "failed deletion preserves task, draft and previous restore slot atomically");
    }

    internal static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }
}
