using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QuickNoteApp.Models;

namespace QuickNoteApp.Services;

/// <summary>
/// Tüm veri yerel SQLite dosyasında tutulur: %AppData%\QuickNoteApp\data.db.
/// </summary>
public class DatabaseService
{
    private readonly string _dbPath;
    private const string CurrentFtsIndexVersion = "1";
    private const string FtsIndexVersionKey = "FtsIndexVersion";
    private const string FtsIndexBuiltAtKey = "FtsIndexBuiltAt";

    private readonly string _connectionString;

    public DatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "QuickNoteApp");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "data.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public DatabaseService(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _dbPath = dbPath;
        _connectionString = $"Data Source={_dbPath}";
    }

    public string AttachmentsDirectory
    {
        get
        {
            var dir = Path.Combine(Path.GetDirectoryName(_dbPath)!, "Attachments");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void Initialize()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            pragmaCmd.ExecuteNonQuery();
        }

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Notes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL DEFAULT '',
                Text TEXT NOT NULL,
                Tags TEXT NOT NULL DEFAULT '',
                ImagePath TEXT NULL,
                IsDone INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 0,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS Notifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                AppName TEXT NOT NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                ReceivedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Reminders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                NoteId INTEGER NULL,
                Title TEXT NOT NULL,
                Text TEXT NOT NULL,
                ReminderAt TEXT NOT NULL,
                IsShown INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                SourceType TEXT NULL,
                SourceKey TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AppMeta (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Tags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                Color TEXT NOT NULL DEFAULT '#6C63FF'
            );

            CREATE TABLE IF NOT EXISTS NoteTags (
                NoteId INTEGER NOT NULL,
                TagId INTEGER NOT NULL,
                PRIMARY KEY (NoteId, TagId),
                FOREIGN KEY (NoteId) REFERENCES Notes(Id) ON DELETE CASCADE,
                FOREIGN KEY (TagId) REFERENCES Tags(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Notes_CreatedAt ON Notes (CreatedAt);
            CREATE INDEX IF NOT EXISTS IX_Notifications_ReceivedAt ON Notifications (ReceivedAt);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Notifications_Unique ON Notifications (AppName, Title, Body, ReceivedAt);
            CREATE INDEX IF NOT EXISTS IX_Reminders_ReminderAt ON Reminders (ReminderAt);

            CREATE VIRTUAL TABLE IF NOT EXISTS NotesFts USING fts5(
                NoteId UNINDEXED,
                Title,
                Text,
                Tags,
                SearchText,
                tokenize='unicode61 remove_diacritics 2'
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS NotificationsFts USING fts5(
                NotificationId UNINDEXED,
                AppName,
                Title,
                Body,
                SearchText,
                tokenize='unicode61 remove_diacritics 2'
            );
            """;
        cmd.ExecuteNonQuery();
        EnsureNoteColumn(conn, "Title", "TEXT NOT NULL DEFAULT ''");
        EnsureNoteColumn(conn, "Tags", "TEXT NOT NULL DEFAULT ''");
        EnsureNoteColumn(conn, "ImagePath", "TEXT NULL");
        EnsureNoteColumn(conn, "IsPinned", "INTEGER NOT NULL DEFAULT 0");
        EnsureNoteColumn(conn, "IsDeleted", "INTEGER NOT NULL DEFAULT 0");
        EnsureNoteColumn(conn, "UpdatedAt", "TEXT NULL");
        EnsureReminderColumn(conn, "SourceType", "TEXT NULL");
        EnsureReminderColumn(conn, "SourceKey", "TEXT NULL");
        EnsureReminderSourceIndex(conn);
        EnsureFtsIndexes(conn);
    }

    public int AddNote(string title, string text, string? imagePath = null, List<string>? tagsList = null)
    {
        var tags = tagsList != null && tagsList.Count > 0 ? string.Join(" ", tagsList.Select(t => t.StartsWith("#") ? t : "#" + t)) : GenerateTags(title, text);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Notes (Title, Text, Tags, ImagePath, IsDone, CreatedAt) VALUES ($title, $text, $tags, $imagePath, 0, $createdAt)";
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$text", text.Trim());

        cmd.Parameters.AddWithValue("$tags", tags);
        cmd.Parameters.AddWithValue("$imagePath", string.IsNullOrWhiteSpace(imagePath) ? DBNull.Value : imagePath);
        cmd.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
        var id = GetLastInsertRowId(conn);
        UpsertNoteFts(conn, id, title.Trim(), text.Trim(), tags);

        if (tagsList != null && tagsList.Count > 0)
        {
            SetNoteTags(id, tagsList);
        }
        else
        {
            var autoTags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).ToList();
            if (autoTags.Count > 0)
            {
                SetNoteTags(id, autoTags);
            }
        }
        return id;
    }

    public void UpdateNote(int noteId, string title, string text, string? imagePath, List<string>? tagsList = null)
    {
        var tags = tagsList != null && tagsList.Count > 0 ? string.Join(" ", tagsList.Select(t => t.StartsWith("#") ? t : "#" + t)) : GenerateTags(title, text);
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notes SET Title = $title, Text = $text, Tags = $tags, ImagePath = $imagePath, UpdatedAt = $updatedAt WHERE Id = $id";
        cmd.Parameters.AddWithValue("$title", title.Trim());
        cmd.Parameters.AddWithValue("$text", text.Trim());
        cmd.Parameters.AddWithValue("$tags", tags);
        cmd.Parameters.AddWithValue("$imagePath", string.IsNullOrWhiteSpace(imagePath) ? DBNull.Value : imagePath);
        cmd.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$id", noteId);
        cmd.ExecuteNonQuery();
        UpsertNoteFts(conn, noteId, title.Trim(), text.Trim(), tags);

        if (tagsList != null && tagsList.Count > 0)
        {
            SetNoteTags(noteId, tagsList);
        }
        else
        {
            var autoTags = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.TrimStart('#')).ToList();
            SetNoteTags(noteId, autoTags);
        }
    }

    public void SetNoteDone(int noteId, bool isDone)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notes SET IsDone = $isDone WHERE Id = $id";
        cmd.Parameters.AddWithValue("$isDone", isDone ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", noteId);
        cmd.ExecuteNonQuery();
    }
    public void DeleteNote(int noteId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notes SET IsDeleted = 1, UpdatedAt = $now WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", noteId);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void ToggleNotePin(int noteId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Notes SET IsPinned = 1 - IsPinned, UpdatedAt = $now WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", noteId);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<NoteItem> GetNotesForDate(DateTime date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Text, Tags, ImagePath, IsDone, CreatedAt, IsPinned, IsDeleted, UpdatedAt FROM Notes WHERE IsDeleted = 0 AND SUBSTR(CreatedAt, 1, 10) = $date ORDER BY IsPinned DESC, CreatedAt ASC";
        cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        return ReadNotes(cmd);
    }

    public List<NoteItem> GetRecentNotes(int limit = 100)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Text, Tags, ImagePath, IsDone, CreatedAt, IsPinned, IsDeleted, UpdatedAt FROM Notes WHERE IsDeleted = 0 ORDER BY IsPinned DESC, CreatedAt DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadNotes(cmd);
    }

    public List<NoteItem> SearchNotes(string query, int limit = 300)
    {
        var cleanedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(cleanedQuery))
            return GetRecentNotes(100);

        var ftsQuery = SqliteFtsSearch.BuildMatchQuery(cleanedQuery);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return GetRecentNotes(100);

        var result = SearchNotesByFts(ftsQuery, limit);
        if (result.Count == 0)
        {
            var anyMatchQuery = SqliteFtsSearch.BuildAnyMatchQuery(cleanedQuery);
            if (!string.IsNullOrWhiteSpace(anyMatchQuery) && !anyMatchQuery.Equals(ftsQuery, StringComparison.Ordinal))
                result = SearchNotesByFts(anyMatchQuery, limit);
        }

        if (result.Count > 0)
            return result;

        var recentGeminiNotes = SearchRecentGeminiNotesForQuestion(cleanedQuery, limit);
        return recentGeminiNotes.Count > 0 ? recentGeminiNotes : SearchNotesInMemory(cleanedQuery, limit);
    }

    public List<NotificationLogItem> SearchNotifications(string query, int limit = 300)
    {
        var cleanedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleanedQuery))
            return GetRecentNotifications(Math.Min(limit, 100));

        var ftsQuery = SqliteFtsSearch.BuildMatchQuery(cleanedQuery);
        if (string.IsNullOrWhiteSpace(ftsQuery))
            return SearchNotificationsInMemory(cleanedQuery, limit);

        var result = SearchNotificationsByFts(ftsQuery, limit);
        if (result.Count == 0)
        {
            var anyMatchQuery = SqliteFtsSearch.BuildAnyMatchQuery(cleanedQuery);
            if (!string.IsNullOrWhiteSpace(anyMatchQuery) && !anyMatchQuery.Equals(ftsQuery, StringComparison.Ordinal))
                result = SearchNotificationsByFts(anyMatchQuery, limit);
        }

        return result.Count > 0 ? result : SearchNotificationsInMemory(cleanedQuery, limit);
    }
    public void AddNotification(string appName, string title, string body)
    {
        AddNotification(appName, title, body, DateTime.Now);
    }

    public void AddNotification(string appName, string title, string body, DateTime receivedAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Notifications (AppName, Title, Body, ReceivedAt) VALUES ($app, $title, $body, $receivedAt)";
        cmd.Parameters.AddWithValue("$app", appName);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$body", body);
        cmd.Parameters.AddWithValue("$receivedAt", receivedAt.ToString("O"));

        var affected = cmd.ExecuteNonQuery();
        if (affected > 0)
        {
            UpsertNotificationFts(conn, GetLastInsertRowId(conn), appName, title, body);
        }
    }

    public List<NotificationLogItem> GetNotificationsForDate(DateTime date)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, AppName, Title, Body, ReceivedAt FROM Notifications WHERE SUBSTR(ReceivedAt, 1, 10) = $date ORDER BY ReceivedAt ASC";
        cmd.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        return ReadNotifications(cmd);
    }

    public List<NotificationLogItem> GetRecentNotifications(int limit = 100)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, AppName, Title, Body, ReceivedAt FROM Notifications ORDER BY ReceivedAt DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadNotifications(cmd);
    }

    private static List<NotificationLogItem> ReadNotifications(SqliteCommand cmd)
    {
        var result = new List<NotificationLogItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new NotificationLogItem
            {
                Id = reader.GetInt32(0),
                AppName = reader.GetString(1),
                Title = reader.GetString(2),
                Body = reader.GetString(3),
                ReceivedAt = DateTime.Parse(reader.GetString(4))
            });
        }

        return result;
    }

    private List<NoteItem> SearchNotesByFts(string ftsQuery, int limit)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Text, n.Tags, n.ImagePath, n.IsDone, n.CreatedAt, n.IsPinned, n.IsDeleted, n.UpdatedAt
            FROM NotesFts f
            JOIN Notes n ON n.Id = f.NoteId
            WHERE n.IsDeleted = 0 AND NotesFts MATCH $query
            ORDER BY n.IsPinned DESC, rank, n.CreatedAt DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", ftsQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadNotes(cmd);
    }

    private List<NotificationLogItem> SearchNotificationsByFts(string ftsQuery, int limit)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.AppName, n.Title, n.Body, n.ReceivedAt
            FROM NotificationsFts f
            JOIN Notifications n ON n.Id = f.NotificationId
            WHERE NotificationsFts MATCH $query
            ORDER BY rank, n.ReceivedAt DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$query", ftsQuery);
        cmd.Parameters.AddWithValue("$limit", limit);

        return ReadNotifications(cmd);
    }

    private List<NoteItem> SearchNotesInMemory(string query, int limit)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return GetRecentNotes(limit)
            .Where(note => words.All(word => NoteMatches(note, word.TrimStart('#'))))
            .ToList();
    }

    private List<NotificationLogItem> SearchNotificationsInMemory(string query, int limit)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return GetRecentNotifications(limit)
            .Where(notification => words.All(word => NotificationMatches(notification, word.TrimStart('#'))))
            .ToList();
    }

    private List<NoteItem> SearchRecentGeminiNotesForQuestion(string query, int limit)
    {
        if (!LooksLikeAmountQuestion(query))
            return new List<NoteItem>();

        return AddRecentAmountNotes(new List<NoteItem>(), limit);
    }

    private List<NoteItem> AddRecentAmountNotes(List<NoteItem> notes, int limit)
    {
        var existingIds = notes.Select(note => note.Id).ToHashSet();
        var amountNotes = GetRecentNotes(Math.Max(limit, 50))
            .Where(note => note.Title.Contains("Gemini", StringComparison.OrdinalIgnoreCase)
                || note.Text.Contains("TL", StringComparison.OrdinalIgnoreCase)
                || note.Text.Contains("tutar", StringComparison.OrdinalIgnoreCase)
                || note.Text.Contains("toplam", StringComparison.OrdinalIgnoreCase))
            .Where(note => existingIds.Add(note.Id))
            .Take(limit)
            .ToList();

        notes.AddRange(amountNotes);
        return notes
            .OrderByDescending(note => note.CreatedAt)
            .Take(limit)
            .ToList();
    }

    private static bool LooksLikeAmountQuestion(string query)
    {
        return ContainsAny(query, "toplam", "tutar", "tl", "ücret", "ucret", "para", "ne kadar", "nekadar");
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool NoteMatches(NoteItem note, string query)
    {
        return note.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Text.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.CreatedAt.ToString("dd.MM.yyyy HH:mm").Contains(query, StringComparison.OrdinalIgnoreCase)
            || note.CreatedAt.ToString("yyyy-MM-dd").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NotificationMatches(NotificationLogItem notification, string query)
    {
        return notification.AppName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || notification.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || notification.Body.Contains(query, StringComparison.OrdinalIgnoreCase)
            || notification.ReceivedAt.ToString("dd.MM.yyyy HH:mm").Contains(query, StringComparison.OrdinalIgnoreCase)
            || notification.ReceivedAt.ToString("yyyy-MM-dd").Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string GenerateTags(string title, string text)
    {
        var source = $"{title} {text}".ToLowerInvariant();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ama", "bir", "bu", "da", "de", "diye", "gibi", "ile", "icin", "için", "olan", "olarak", "sonra", "şu", "the", "and",
            "merhaba", "tesekkurler", "teşekkürler", "sayin", "sayın", "hakkinda", "hakkında", "dosya", "not", "konu"
        };

        var tags = Regex.Matches(source, @"[\p{L}\p{N}]{4,}")
            .Select(match => NormalizeTurkishToken(match.Value))
            .Where(word => word.Length >= 4 && !stopWords.Contains(word))
            .GroupBy(word => word)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Take(6)
            .Select(group => "#" + group.Key)
            .ToList();

        return string.Join(" ", tags);
    }

    private static string NormalizeTurkishToken(string value)
    {
        return value
            .Replace("ı", "i")
            .Replace("ğ", "g")
            .Replace("ü", "u")
            .Replace("ş", "s")
            .Replace("ö", "o")
            .Replace("ç", "c");
    }

    private static string CreateFallbackTitle(string text)
    {
        var firstLine = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
            return "Başlıksız not";

        return firstLine.Length <= 60 ? firstLine : firstLine[..60] + "...";
    }

    private static void EnsureNoteColumn(SqliteConnection conn, string name, string definition)
    {
        try
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE Notes ADD COLUMN {name} {definition}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    private static void EnsureReminderColumn(SqliteConnection conn, string name, string definition)
    {
        try
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE Reminders ADD COLUMN {name} {definition}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
        }
    }


    private static void EnsureReminderSourceIndex(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_Reminders_SourceUnique ON Reminders (SourceType, SourceKey) WHERE SourceType IS NOT NULL AND SourceType <> '' AND SourceKey IS NOT NULL AND SourceKey <> ''";
        cmd.ExecuteNonQuery();
    }


    private static void EnsureFtsIndexes(SqliteConnection conn)
    {
        var countsMatch = FtsRowCountsMatch(conn);
        if (countsMatch)
        {
            if (GetMetaValue(conn, FtsIndexVersionKey) != CurrentFtsIndexVersion)
                SetMetaValue(conn, FtsIndexVersionKey, CurrentFtsIndexVersion);

            if (string.IsNullOrWhiteSpace(GetMetaValue(conn, FtsIndexBuiltAtKey)))
                SetMetaValue(conn, FtsIndexBuiltAtKey, DateTimeOffset.UtcNow.ToString("O"));

            return;
        }

        RebuildFtsIndexes(conn);
        SetMetaValue(conn, FtsIndexVersionKey, CurrentFtsIndexVersion);
        SetMetaValue(conn, FtsIndexBuiltAtKey, DateTimeOffset.UtcNow.ToString("O"));
    }

    private static bool FtsRowCountsMatch(SqliteConnection conn)
    {
        return CountRows(conn, "Notes") == CountRows(conn, "NotesFts")
            && CountRows(conn, "Notifications") == CountRows(conn, "NotificationsFts");
    }

    private static long CountRows(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string? GetMetaValue(SqliteConnection conn, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppMeta WHERE Key = $key";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    private static void SetMetaValue(SqliteConnection conn, string key, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO AppMeta (Key, Value)
            VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }
    private static void RebuildFtsIndexes(SqliteConnection conn)
    {
        RebuildNotesFts(conn);
        RebuildNotificationsFts(conn);
    }

    private static void RebuildNotesFts(SqliteConnection conn)
    {
        using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM NotesFts";
        delete.ExecuteNonQuery();

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT Id, Title, Text, Tags FROM Notes";

        var rows = new List<(int Id, string Title, string Text, string Tags)>();
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        foreach (var row in rows)
            UpsertNoteFts(conn, row.Id, row.Title, row.Text, row.Tags);
    }

    private static void RebuildNotificationsFts(SqliteConnection conn)
    {
        using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM NotificationsFts";
        delete.ExecuteNonQuery();

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT Id, AppName, Title, Body FROM Notifications";

        var rows = new List<(int Id, string AppName, string Title, string Body)>();
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
                rows.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        foreach (var row in rows)
            UpsertNotificationFts(conn, row.Id, row.AppName, row.Title, row.Body);
    }

    private static int GetLastInsertRowId(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void UpsertNoteFts(SqliteConnection conn, int noteId, string title, string text, string tags)
    {
        DeleteNoteFts(conn, noteId);

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO NotesFts (NoteId, Title, Text, Tags, SearchText) VALUES ($id, $title, $text, $tags, $searchText)";
        insert.Parameters.AddWithValue("$id", noteId);
        insert.Parameters.AddWithValue("$title", title);
        insert.Parameters.AddWithValue("$text", text);
        insert.Parameters.AddWithValue("$tags", tags);
        insert.Parameters.AddWithValue("$searchText", SqliteFtsSearch.BuildSearchText(title, text, tags));
        insert.ExecuteNonQuery();
    }

    private static void DeleteNoteFts(SqliteConnection conn, int noteId)
    {
        using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM NotesFts WHERE NoteId = $id";
        delete.Parameters.AddWithValue("$id", noteId);
        delete.ExecuteNonQuery();
    }

    private static void UpsertNotificationFts(SqliteConnection conn, int notificationId, string appName, string title, string body)
    {
        using var delete = conn.CreateCommand();
        delete.CommandText = "DELETE FROM NotificationsFts WHERE NotificationId = $id";
        delete.Parameters.AddWithValue("$id", notificationId);
        delete.ExecuteNonQuery();

        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO NotificationsFts (NotificationId, AppName, Title, Body, SearchText) VALUES ($id, $appName, $title, $body, $searchText)";
        insert.Parameters.AddWithValue("$id", notificationId);
        insert.Parameters.AddWithValue("$appName", appName);
        insert.Parameters.AddWithValue("$title", title);
        insert.Parameters.AddWithValue("$body", body);
        insert.Parameters.AddWithValue("$searchText", SqliteFtsSearch.BuildSearchText(appName, title, body));
        insert.ExecuteNonQuery();
    }

    public string ExecuteReadOnlyQuery(string sqlQuery)
    {
        var trimmed = sqlQuery.TrimStart();
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Yalnızca okuma (SELECT) sorgularına izin verilmektedir.");
        }

        // Salt okunur bağlantı dizesi
        var roConnectionString = $"Data Source={_dbPath};Mode=ReadOnly;";
        
        using var conn = new SqliteConnection(roConnectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sqlQuery;

        using var reader = cmd.ExecuteReader();
        var builder = new System.Text.StringBuilder();
        
        var columns = new System.Collections.Generic.List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        if (columns.Count == 0)
        {
            return "Sorgu hiçbir kolon döndürmedi.";
        }

        // Markdown tablosu başlığı
        builder.AppendLine("| " + string.Join(" | ", columns) + " |");
        builder.AppendLine("| " + string.Join(" | ", columns.Select(_ => "---")) + " |");

        int rowCount = 0;
        while (reader.Read())
        {
            var rowValues = new System.Collections.Generic.List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i);
                var valStr = val == DBNull.Value ? "NULL" : val.ToString();
                valStr = valStr?.Replace("\r", " ").Replace("\n", " ").Replace("|", "\\|") ?? "NULL";
                rowValues.Add(valStr);
            }
            builder.AppendLine("| " + string.Join(" | ", rowValues) + " |");
            rowCount++;
        }

        if (rowCount == 0)
        {
            return "Sorgu başarılı, ancak hiçbir kayıt bulunamadı.";
        }

        return builder.ToString();
    }

    private static (DateTime start, DateTime end) DayRange(DateTime date)
    {
        var start = date.Date;
        return (start, start.AddDays(1));
    }

    public List<NoteTag> GetAllTags()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Color FROM Tags ORDER BY Name ASC";
        var result = new List<NoteTag>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new NoteTag
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Color = reader.GetString(2)
            });
        }
        return result;
    }

    public NoteTag GetOrCreateTag(string name, string color = "#6C63FF")
    {
        name = name.Trim().TrimStart('#');
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Etiket adı boş olamaz.");

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        
        var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT Id, Name, Color FROM Tags WHERE Name = $name";
        selectCmd.Parameters.AddWithValue("$name", name);
        using (var reader = selectCmd.ExecuteReader())
        {
            if (reader.Read())
            {
                return new NoteTag
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                };
            }
        }

        var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO Tags (Name, Color) VALUES ($name, $color); SELECT last_insert_rowid();";
        insertCmd.Parameters.AddWithValue("$name", name);
        insertCmd.Parameters.AddWithValue("$color", color);
        var id = Convert.ToInt32(insertCmd.ExecuteScalar());

        return new NoteTag
        {
            Id = id,
            Name = name,
            Color = color
        };
    }

    public void AddTagToNote(int noteId, int tagId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO NoteTags (NoteId, TagId) VALUES ($noteId, $tagId)";
        cmd.Parameters.AddWithValue("$noteId", noteId);
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.ExecuteNonQuery();
    }

    public void RemoveTagFromNote(int noteId, int tagId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM NoteTags WHERE NoteId = $noteId AND TagId = $tagId";
        cmd.Parameters.AddWithValue("$noteId", noteId);
        cmd.Parameters.AddWithValue("$tagId", tagId);
        cmd.ExecuteNonQuery();
    }

    public void SetNoteTags(int noteId, List<string> tagNames)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var transaction = conn.BeginTransaction();

        var deleteCmd = conn.CreateCommand();
        deleteCmd.Transaction = transaction;
        deleteCmd.CommandText = "DELETE FROM NoteTags WHERE NoteId = $noteId";
        deleteCmd.Parameters.AddWithValue("$noteId", noteId);
        deleteCmd.ExecuteNonQuery();

        var tagIds = new List<int>();
        foreach (var name in tagNames)
        {
            var cleanName = name.Trim().TrimStart('#');
            if (string.IsNullOrWhiteSpace(cleanName)) continue;

            int tagId;
            var checkCmd = conn.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM Tags WHERE Name = $name";
            checkCmd.Parameters.AddWithValue("$name", cleanName);
            var result = checkCmd.ExecuteScalar();
            if (result != null)
            {
                tagId = Convert.ToInt32(result);
            }
            else
            {
                var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = "INSERT INTO Tags (Name, Color) VALUES ($name, '#6C63FF'); SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("$name", cleanName);
                tagId = Convert.ToInt32(insertCmd.ExecuteScalar());
            }

            tagIds.Add(tagId);
        }

        foreach (var tagId in tagIds)
        {
            var insertRelationCmd = conn.CreateCommand();
            insertRelationCmd.Transaction = transaction;
            insertRelationCmd.CommandText = "INSERT OR IGNORE INTO NoteTags (NoteId, TagId) VALUES ($noteId, $tagId)";
            insertRelationCmd.Parameters.AddWithValue("$noteId", noteId);
            insertRelationCmd.Parameters.AddWithValue("$tagId", tagId);
            insertRelationCmd.ExecuteNonQuery();
        }

        transaction.Commit();

        var tagsString = string.Join(" ", tagNames.Select(t => t.StartsWith("#") ? t : "#" + t));
        var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = "UPDATE Notes SET Tags = $tags WHERE Id = $id";
        updateCmd.Parameters.AddWithValue("$tags", tagsString);
        updateCmd.Parameters.AddWithValue("$id", noteId);
        updateCmd.ExecuteNonQuery();

        var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT Title, Text FROM Notes WHERE Id = $id";
        selectCmd.Parameters.AddWithValue("$id", noteId);
        using var reader = selectCmd.ExecuteReader();
        if (reader.Read())
        {
            var title = reader.GetString(0);
            var text = reader.GetString(1);
            UpsertNoteFts(conn, noteId, title, text, tagsString);
        }
    }

    public List<NoteTag> GetTagsForNote(int noteId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.Id, t.Name, t.Color 
            FROM Tags t
            JOIN NoteTags nt ON t.Id = nt.TagId
            WHERE nt.NoteId = $noteId
            ORDER BY t.Name ASC
            """;
        cmd.Parameters.AddWithValue("$noteId", noteId);
        var result = new List<NoteTag>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new NoteTag
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Color = reader.GetString(2)
            });
        }
        return result;
    }



    public List<NoteItem> GetNotesByTag(string tagName)
    {
        tagName = tagName.Trim().TrimStart('#');
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.Id, n.Title, n.Text, n.Tags, n.ImagePath, n.IsDone, n.CreatedAt, n.IsPinned, n.IsDeleted, n.UpdatedAt
            FROM Notes n
            JOIN NoteTags nt ON n.Id = nt.NoteId
            JOIN Tags t ON nt.TagId = t.Id
            WHERE n.IsDeleted = 0 AND t.Name = $tagName
            ORDER BY n.IsPinned DESC, n.CreatedAt DESC
            """;
        cmd.Parameters.AddWithValue("$tagName", tagName);

        return ReadNotes(cmd);
    }

    public void DeleteTag(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Tags WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateTagColor(int id, string color)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Tags SET Color = $color WHERE Id = $id";
        cmd.Parameters.AddWithValue("$color", color);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public NoteItem? GetNoteById(int noteId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Text, Tags, ImagePath, IsDone, CreatedAt, IsPinned, IsDeleted, UpdatedAt FROM Notes WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", noteId);
        
        var notes = ReadNotes(cmd);
        return notes.Count > 0 ? notes[0] : null;
    }

    public NoteItem? GetNoteByTitle(string title)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Text, Tags, ImagePath, IsDone, CreatedAt, IsPinned, IsDeleted, UpdatedAt FROM Notes WHERE IsDeleted = 0 AND LOWER(Title) = LOWER($title) LIMIT 1";
        cmd.Parameters.AddWithValue("$title", title.Trim());
        
        var notes = ReadNotes(cmd);
        return notes.Count > 0 ? notes[0] : null;
    }

    public List<NoteItem> GetBacklinksForNote(string title)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Text, Tags, ImagePath, IsDone, CreatedAt, IsPinned, IsDeleted, UpdatedAt FROM Notes WHERE IsDeleted = 0 AND Text LIKE $linkPattern";
        cmd.Parameters.AddWithValue("$linkPattern", "%[[" + title.Trim() + "]]%");
        
        return ReadNotes(cmd);
    }
    private static List<NoteItem> ReadNotes(SqliteCommand cmd)
    {
        var result = new List<NoteItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var tags = reader.GetString(3);
            result.Add(new NoteItem
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Text = reader.GetString(2),
                Tags = tags,
                ImagePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                IsDone = reader.GetInt32(5) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                IsPinned = reader.GetInt32(7) == 1,
                IsDeleted = reader.GetInt32(8) == 1,
                UpdatedAt = reader.IsDBNull(9) ? null : DateTime.Parse(reader.GetString(9)),
                TagList = tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(tag => new NoteTag { Name = tag.TrimStart('#') })
                    .ToList()
            });
        }

        return result;
    }

    public void AddReminder(NoteItem note, DateTime reminderAt)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Reminders (NoteId, Title, Text, ReminderAt, IsShown, CreatedAt, SourceType, SourceKey)
            VALUES ($noteId, $title, $text, $reminderAt, 0, $createdAt, NULL, NULL)
            """;
        cmd.Parameters.AddWithValue("$noteId", note.Id);
        cmd.Parameters.AddWithValue("$title", note.Title);
        cmd.Parameters.AddWithValue("$text", note.Text);
        cmd.Parameters.AddWithValue("$reminderAt", reminderAt.ToString("O"));
        cmd.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void UpsertCalendarReminder(CalendarEvent calendarEvent, DateTime reminderAt)
    {
        var sourceKey = BuildCalendarReminderKey(calendarEvent);
        var title = string.IsNullOrWhiteSpace(calendarEvent.Summary)
            ? "Google Takvim Etkinligi"
            : calendarEvent.Summary.Trim();
        var text = BuildCalendarReminderText(calendarEvent);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = """
            UPDATE Reminders
            SET Title = $title,
                Text = $text,
                ReminderAt = $reminderAt,
                IsShown = 0
            WHERE SourceType = 'GoogleCalendar' AND SourceKey = $sourceKey
            """;
        updateCmd.Parameters.AddWithValue("$title", title);
        updateCmd.Parameters.AddWithValue("$text", text);
        updateCmd.Parameters.AddWithValue("$reminderAt", reminderAt.ToString("O"));
        updateCmd.Parameters.AddWithValue("$sourceKey", sourceKey);

        var affected = updateCmd.ExecuteNonQuery();
        if (affected > 0)
            return;

        var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT INTO Reminders (NoteId, Title, Text, ReminderAt, IsShown, CreatedAt, SourceType, SourceKey)
            VALUES (NULL, $title, $text, $reminderAt, 0, $createdAt, 'GoogleCalendar', $sourceKey)
            """;
        insertCmd.Parameters.AddWithValue("$title", title);
        insertCmd.Parameters.AddWithValue("$text", text);
        insertCmd.Parameters.AddWithValue("$reminderAt", reminderAt.ToString("O"));
        insertCmd.Parameters.AddWithValue("$createdAt", DateTime.Now.ToString("O"));
        insertCmd.Parameters.AddWithValue("$sourceKey", sourceKey);
        insertCmd.ExecuteNonQuery();
    }

    public List<ReminderItem> GetDueReminders(DateTime now)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, NoteId, Title, Text, ReminderAt, IsShown, CreatedAt, SourceType, SourceKey
            FROM Reminders
            WHERE IsShown = 0 AND ReminderAt <= $now
            ORDER BY ReminderAt ASC
            """;
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        return ReadReminders(cmd);
    }

    public List<ReminderItem> GetUpcomingReminders(int limit = 5)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, NoteId, Title, Text, ReminderAt, IsShown, CreatedAt, SourceType, SourceKey
            FROM Reminders
            WHERE IsShown = 0
            ORDER BY ReminderAt ASC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        return ReadReminders(cmd);
    }

    public void MarkReminderShown(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Reminders SET IsShown = 1 WHERE Id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<ReminderItem> ReadReminders(SqliteCommand cmd)
    {
        var result = new List<ReminderItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ReminderItem
            {
                Id = reader.GetInt32(0),
                NoteId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Title = reader.GetString(2),
                Text = reader.GetString(3),
                ReminderAt = DateTime.Parse(reader.GetString(4)),
                IsShown = reader.GetInt32(5) == 1,
                CreatedAt = DateTime.Parse(reader.GetString(6)),
                SourceType = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                SourceKey = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
            });
        }

        return result;
    }

    private static string BuildCalendarReminderKey(CalendarEvent calendarEvent)
    {
        if (!string.IsNullOrWhiteSpace(calendarEvent.ExternalId))
            return calendarEvent.ExternalId.Trim();

        return $"{calendarEvent.Summary}|{calendarEvent.StartTime:O}|{calendarEvent.EndTime:O}";
    }

    private static string BuildCalendarReminderText(CalendarEvent calendarEvent)
    {
        var timeText = calendarEvent.IsAllDay
            ? $"Tarih: {calendarEvent.StartTime:dd.MM.yyyy} (Tum gun)"
            : $"Saat: {calendarEvent.StartTime:dd.MM.yyyy HH:mm} - {calendarEvent.EndTime:HH:mm}";

        var parts = new List<string> { timeText };
        if (!string.IsNullOrWhiteSpace(calendarEvent.Location))
            parts.Add("Konum: " + calendarEvent.Location.Trim());
        if (!string.IsNullOrWhiteSpace(calendarEvent.Description))
            parts.Add(calendarEvent.Description.Trim());

        return string.Join(Environment.NewLine, parts);
    }

    public string BuildGeminiDatabaseContext(string question, int limit = 100)
    {
        var notes = SearchNotes(question, limit);
        var notifications = GetRecentNotifications(limit)
            .Where(notification => NotificationFilter.ShouldCapture(notification.AppName, notification.Title, notification.Body))
            .Where(notification => NotificationMatches(notification, question))
            .Take(limit)
            .ToList();

        if (notifications.Count == 0)
        {
            notifications = GetRecentNotifications(Math.Min(limit, 30))
                .Where(notification => NotificationFilter.ShouldCapture(notification.AppName, notification.Title, notification.Body))
                .ToList();
        }

        return "Yerel veritabani" + Environment.NewLine + SearchContextFormatter.Format(notes, notifications);
    }

    public int CleanUnallowedNotifications()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        
        var all = GetRecentNotifications(5000);
        var toDelete = all.Where(n => !NotificationFilter.ShouldCapture(n.AppName, n.Title, n.Body)).ToList();
        
        if (toDelete.Count == 0)
            return 0;
            
        using var transaction = conn.BeginTransaction();
        try
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "DELETE FROM Notifications WHERE Id = $id";
            var idParam = cmd.Parameters.Add("$id", SqliteType.Integer);
            
            foreach (var item in toDelete)
            {
                idParam.Value = item.Id;
                cmd.ExecuteNonQuery();
            }
            
            var ftsCmd = conn.CreateCommand();
            ftsCmd.Transaction = transaction;
            ftsCmd.CommandText = "DELETE FROM NotificationsFts WHERE NotificationId = $id";
            var ftsIdParam = ftsCmd.Parameters.Add("$id", SqliteType.Integer);
            
            foreach (var item in toDelete)
            {
                ftsIdParam.Value = item.Id;
                ftsCmd.ExecuteNonQuery();
            }
            
            transaction.Commit();
            return toDelete.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}






