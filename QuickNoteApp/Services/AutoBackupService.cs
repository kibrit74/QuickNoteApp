using System.IO;

namespace QuickNoteApp.Services;

public static class AutoBackupService
{
    private const string BackupDirectoryKey = "AutoBackupDirectory";
    private const string RetentionCountKey = "AutoBackupRetentionCount";
    private const string LastBackupDateKey = "AutoBackupLastDate";
    private const string LastBackupPathKey = "AutoBackupLastPath";
    private const int DefaultRetentionCount = 14;

    public static AutoBackupResult RunStartupBackup(DatabaseService db, DateTime now)
    {
        var today = now.Date.ToString("yyyy-MM-dd");
        if (db.GetMeta(LastBackupDateKey) == today)
        {
            return new AutoBackupResult(false, db.GetMeta(LastBackupPathKey), "Bugun zaten otomatik yedek alindi.");
        }

        var directory = GetBackupDirectory(db);
        Directory.CreateDirectory(directory);

        var backupPath = Path.Combine(directory, $"QuickNoteApp-auto-{now:yyyyMMdd-HHmmss}.db");
        db.BackupDatabase(backupPath);

        db.SetMeta(LastBackupDateKey, today);
        db.SetMeta(LastBackupPathKey, backupPath);
        CleanupOldBackups(directory, GetRetentionCount(db));

        return new AutoBackupResult(true, backupPath, "Otomatik yedek alindi.");
    }

    public static string GetBackupDirectory(DatabaseService db)
    {
        var configured = db.GetMeta(BackupDirectoryKey);
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var databaseDirectory = Path.GetDirectoryName(db.DatabasePath)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(databaseDirectory, "Backups");
    }

    public static void SetBackupDirectory(DatabaseService db, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Yedek klasoru bos olamaz.", nameof(path));

        Directory.CreateDirectory(path);
        db.SetMeta(BackupDirectoryKey, path);
    }

    public static int GetRetentionCount(DatabaseService db)
    {
        var value = db.GetMeta(RetentionCountKey);
        return int.TryParse(value, out var count) && count >= 1 ? count : DefaultRetentionCount;
    }

    public static void SetRetentionCount(DatabaseService db, int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count), "Saklama sayisi en az 1 olmali.");

        db.SetMeta(RetentionCountKey, count.ToString());
        CleanupOldBackups(GetBackupDirectory(db), count);
    }

    public static string? GetLatestBackupPath(DatabaseService db)
    {
        var directory = GetBackupDirectory(db);
        if (!Directory.Exists(directory))
            return null;

        return Directory.GetFiles(directory, "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    public static DateTime? GetLastBackupDate(DatabaseService db)
    {
        var value = db.GetMeta(LastBackupDateKey);
        return DateTime.TryParse(value, out var date) ? date.Date : null;
    }

    private static void CleanupOldBackups(string directory, int retentionCount)
    {
        if (!Directory.Exists(directory))
            return;

        var files = Directory.GetFiles(directory, "*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name)
            .Skip(retentionCount)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Eski yedek silinemese bile yeni yedek alma akisini bozmayalim.
            }
        }
    }
}

public sealed record AutoBackupResult(bool Created, string? BackupPath, string Message);
