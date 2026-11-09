using System.IO;
using Microsoft.Data.Sqlite;
using PrivacyGuard.Core;

namespace PrivacyGuard.App.Services;

public sealed class HistoryRepository
{
    private readonly string _connectionString;

    public HistoryRepository()
    {
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIPrivacyGuard");
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "history.sqlite"),
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        Initialize();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        cmd.ExecuteNonQuery();
        return db;
    }

    private void Initialize()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Sources(
              Id TEXT PRIMARY KEY,
              Path TEXT NOT NULL,
              Hash TEXT NOT NULL,
              Width INTEGER NOT NULL,
              Height INTEGER NOT NULL,
              CreatedAt TEXT NOT NULL,
              UNIQUE(Path, Hash)
            );
            CREATE TABLE IF NOT EXISTS DetectionEvents(
              Id TEXT PRIMARY KEY,
              SourceId TEXT NOT NULL REFERENCES Sources(Id),
              Category TEXT NOT NULL,
              Confidence REAL NULL,
              X REAL NOT NULL, Y REAL NOT NULL, Width REAL NOT NULL, Height REAL NOT NULL,
              Decision INTEGER NOT NULL,
              Origin TEXT NOT NULL,
              CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Detection_Source ON DetectionEvents(SourceId);
            CREATE TABLE IF NOT EXISTS ReviewDecisions(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              EventId TEXT NOT NULL REFERENCES DetectionEvents(Id),
              Decision INTEGER NOT NULL,
              ReviewedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS RegionEdits(
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              EventId TEXT NOT NULL REFERENCES DetectionEvents(Id),
              OldBounds TEXT NOT NULL,
              NewBounds TEXT NOT NULL,
              EditedAt TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Export must use the exact reviewed version, never insert an empty review row.</summary>
    public Guid? FindSource(string path, string hash)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Sources WHERE Path=$path AND Hash=$hash";
        cmd.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        cmd.Parameters.AddWithValue("$hash", hash);
        return cmd.ExecuteScalar() is string found ? Guid.Parse(found) : null;
    }

    public Guid GetOrCreateSource(string path, string hash, int width, int height)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Id FROM Sources WHERE Path=$path AND Hash=$hash";
        cmd.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        cmd.Parameters.AddWithValue("$hash", hash);
        var found = cmd.ExecuteScalar() as string;
        if (found is not null) return Guid.Parse(found);
        var id = Guid.NewGuid();
        cmd.CommandText = "INSERT INTO Sources (Id,Path,Hash,Width,Height,CreatedAt) " +
                          "VALUES ($id,$path,$hash,$width,$height,$created)";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        cmd.Parameters.AddWithValue("$width", width);
        cmd.Parameters.AddWithValue("$height", height);
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return id;
    }

    public IReadOnlyList<DetectionRecord> GetDetections(Guid sourceId)
    {
        var result = new List<DetectionRecord>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT Id,Category,Confidence,X,Y,Width,Height,Decision,Origin,CreatedAt
            FROM DetectionEvents WHERE SourceId=$source ORDER BY CreatedAt, rowid
            """;
        cmd.Parameters.AddWithValue("$source", sourceId.ToString());
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DetectionRecord(
                Guid.Parse(reader.GetString(0)), sourceId, reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                new NormalizedRect(reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6)),
                (ReviewDecision)reader.GetInt32(7), reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9))));
        }
        return result;
    }

    /// <summary>Aggregated model-feedback export only; no source media/path is emitted.</summary>
    public IReadOnlyList<DetectionRecord> GetAllModelDetections()
    {
        var result = new List<DetectionRecord>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT Id,SourceId,Category,Confidence,X,Y,Width,Height,Decision,Origin,CreatedAt
            FROM DetectionEvents WHERE Origin LIKE 'onnx:%' ORDER BY CreatedAt, Id
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new DetectionRecord(
                Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                new NormalizedRect(reader.GetDouble(4), reader.GetDouble(5),
                    reader.GetDouble(6), reader.GetDouble(7)),
                (ReviewDecision)reader.GetInt32(8), reader.GetString(9),
                DateTimeOffset.Parse(reader.GetString(10))));
        }
        return result;
    }

    public void InsertDetection(DetectionRecord r)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO DetectionEvents
            (Id,SourceId,Category,Confidence,X,Y,Width,Height,Decision,Origin,CreatedAt)
            VALUES ($id,$source,$category,$confidence,$x,$y,$w,$h,$decision,$origin,$created)
            """;
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.Parameters.AddWithValue("$source", r.SourceId.ToString());
        cmd.Parameters.AddWithValue("$category", r.Category);
        cmd.Parameters.AddWithValue("$confidence", r.Confidence is null ? DBNull.Value : r.Confidence.Value);
        cmd.Parameters.AddWithValue("$x", r.Bounds.X);
        cmd.Parameters.AddWithValue("$y", r.Bounds.Y);
        cmd.Parameters.AddWithValue("$w", r.Bounds.Width);
        cmd.Parameters.AddWithValue("$h", r.Bounds.Height);
        cmd.Parameters.AddWithValue("$decision", (int)r.Decision);
        cmd.Parameters.AddWithValue("$origin", r.Origin);
        cmd.Parameters.AddWithValue("$created", r.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void InsertDetections(IReadOnlyList<DetectionRecord> detections)
    {
        if (detections.Count == 0) return;
        using var db = Open();
        using var transaction = db.BeginTransaction();
        foreach (var r in detections)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO DetectionEvents
                (Id,SourceId,Category,Confidence,X,Y,Width,Height,Decision,Origin,CreatedAt)
                VALUES ($id,$source,$category,$confidence,$x,$y,$w,$h,$decision,$origin,$created)
                """;
            cmd.Parameters.AddWithValue("$id", r.Id.ToString());
            cmd.Parameters.AddWithValue("$source", r.SourceId.ToString());
            cmd.Parameters.AddWithValue("$category", r.Category);
            cmd.Parameters.AddWithValue("$confidence", r.Confidence is null ? DBNull.Value : r.Confidence.Value);
            cmd.Parameters.AddWithValue("$x", r.Bounds.X);
            cmd.Parameters.AddWithValue("$y", r.Bounds.Y);
            cmd.Parameters.AddWithValue("$w", r.Bounds.Width);
            cmd.Parameters.AddWithValue("$h", r.Bounds.Height);
            cmd.Parameters.AddWithValue("$decision", (int)r.Decision);
            cmd.Parameters.AddWithValue("$origin", r.Origin);
            cmd.Parameters.AddWithValue("$created", r.CreatedAt.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void UpdateDecision(Guid eventId, ReviewDecision decision)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE DetectionEvents SET Decision=$d WHERE Id=$id";
        cmd.Parameters.AddWithValue("$d", (int)decision);
        cmd.Parameters.AddWithValue("$id", eventId.ToString());
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Candidate not found.");
        cmd.CommandText = "INSERT INTO ReviewDecisions(EventId,Decision,ReviewedAt) VALUES($id,$d,$ts)";
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }
    public void UpdateBounds(Guid eventId, NormalizedRect newBounds)
    {
        var bounds = newBounds.Clamp();
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("Detection region must have positive area.", nameof(newBounds));
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var lookup = db.CreateCommand();
        lookup.Transaction = tx;
        lookup.CommandText = "SELECT X,Y,Width,Height FROM DetectionEvents WHERE Id=$id";
        lookup.Parameters.AddWithValue("$id", eventId.ToString());
        string oldBounds;
        using (var reader = lookup.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Candidate not found.");
            oldBounds = System.Text.Json.JsonSerializer.Serialize(new NormalizedRect(
                reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2), reader.GetDouble(3)));
        }
        using var update = db.CreateCommand();
        update.Transaction = tx;
        update.CommandText = "UPDATE DetectionEvents SET X=$x,Y=$y,Width=$w,Height=$h WHERE Id=$id";
        update.Parameters.AddWithValue("$id", eventId.ToString());
        update.Parameters.AddWithValue("$x", bounds.X);
        update.Parameters.AddWithValue("$y", bounds.Y);
        update.Parameters.AddWithValue("$w", bounds.Width);
        update.Parameters.AddWithValue("$h", bounds.Height);
        if (update.ExecuteNonQuery() != 1) throw new InvalidOperationException("Candidate not found.");
        using var audit = db.CreateCommand();
        audit.Transaction = tx;
        audit.CommandText = "INSERT INTO RegionEdits(EventId,OldBounds,NewBounds,EditedAt) VALUES($id,$old,$new,$when)";
        audit.Parameters.AddWithValue("$id", eventId.ToString());
        audit.Parameters.AddWithValue("$old", oldBounds);
        audit.Parameters.AddWithValue("$new", System.Text.Json.JsonSerializer.Serialize(bounds));
        audit.Parameters.AddWithValue("$when", DateTimeOffset.UtcNow.ToString("O"));
        audit.ExecuteNonQuery();
        tx.Commit();
    }

}
