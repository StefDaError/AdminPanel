using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminPanel.Models;
using Microsoft.Data.Sqlite;

namespace AdminPanel.Database;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private const string DateFmt = "yyyy-MM-dd HH:mm:ss";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DatabaseService(string? dbPath = null)
    {
        if (string.IsNullOrEmpty(dbPath))
        {

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "AdminPanel");
            Directory.CreateDirectory(folder);
            dbPath = Path.Combine(folder, "admin.db");
        }

        _connectionString = $"Data Source={dbPath}";
    }

    public void Initialize()
    {

        SQLitePCL.Batteries_V2.Init();


        using var conn = new SqliteConnection(_connectionString);
        conn.Open();


        var sql = @"
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS Nodes (
                NodeId     TEXT PRIMARY KEY,
                DomainName TEXT NOT NULL UNIQUE,
                IpAddress  TEXT,
                Platform   TEXT,
                OsVersion  TEXT,
                IsMaster   INTEGER NOT NULL DEFAULT 0,
                LastSeen   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_nodes_lastseen ON Nodes(LastSeen);

            CREATE TABLE IF NOT EXISTS Snapshots (
                SnapshotId INTEGER PRIMARY KEY AUTOINCREMENT,
                NodeId     TEXT NOT NULL,
                CapturedAt TEXT NOT NULL,
                FOREIGN KEY (NodeId) REFERENCES Nodes(NodeId) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_snapshots_node ON Snapshots(NodeId);
            CREATE INDEX IF NOT EXISTS idx_snapshots_time ON Snapshots(CapturedAt);

            CREATE TABLE IF NOT EXISTS Components (
                ComponentId  INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotId   INTEGER NOT NULL,
                NodeId       TEXT NOT NULL,
                Category     TEXT NOT NULL,
                Name         TEXT,
                Vendor       TEXT,
                SerialNumber TEXT,
                Specs        TEXT,
                CapturedAt   TEXT NOT NULL,
                FOREIGN KEY (SnapshotId) REFERENCES Snapshots(SnapshotId) ON DELETE CASCADE,
                FOREIGN KEY (NodeId)     REFERENCES Nodes(NodeId)         ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_components_snap ON Components(SnapshotId);
            CREATE INDEX IF NOT EXISTS idx_components_node_cat ON Components(NodeId, Category);

            CREATE TABLE IF NOT EXISTS OsInfo (
                OsInfoId     INTEGER PRIMARY KEY AUTOINCREMENT,
                SnapshotId   INTEGER NOT NULL,
                NodeId       TEXT NOT NULL,
                Caption      TEXT,
                Version      TEXT,
                Architecture TEXT,
                CapturedAt   TEXT NOT NULL,
                FOREIGN KEY (SnapshotId) REFERENCES Snapshots(SnapshotId) ON DELETE CASCADE,
                FOREIGN KEY (NodeId)     REFERENCES Nodes(NodeId)         ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_osinfo_snap ON OsInfo(SnapshotId);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    public void SaveFromJson(string json, string ipAddress, string platform)
    {
        MessageEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<MessageEnvelope>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Ошибка парсинга JSON: {ex.Message}", ex);
        }

        if (envelope?.Payload is null)
            throw new InvalidOperationException("Конверт не содержит payload.");

        Save(envelope.AgentId, envelope.Payload, ipAddress, platform);
    }

    private void Save(string agentId, HardwareInfo hw, string ipAddress, string platform)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        var now = DateTime.Now;

        string nodeId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT NodeId FROM Nodes WHERE DomainName = $dn";
            cmd.Parameters.AddWithValue("$dn", agentId);
            nodeId = cmd.ExecuteScalar()?.ToString() ?? Guid.NewGuid().ToString();
        }

        Exec(conn, tx, @"
            INSERT INTO Nodes (NodeId, DomainName, IpAddress, Platform, OsVersion, IsMaster, LastSeen)
            VALUES ($id, $dn, $ip, $pl, $osv, 0, $ls)
            ON CONFLICT(DomainName) DO UPDATE SET
                IpAddress = excluded.IpAddress,
                Platform  = excluded.Platform,
                OsVersion = excluded.OsVersion,
                LastSeen  = excluded.LastSeen;",
            ("$id", nodeId), ("$dn", agentId), ("$ip", ipAddress), ("$pl", platform),
            ("$osv", $"{hw.Os.Caption} {hw.Os.Version}".Trim()),
            ("$ls", now.ToString(DateFmt)));

        int snapshotId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Snapshots (NodeId, CapturedAt) VALUES ($nodeId, $capturedAt);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$nodeId", nodeId);
            cmd.Parameters.AddWithValue("$capturedAt", now.ToString(DateFmt));
            snapshotId = Convert.ToInt32(cmd.ExecuteScalar());
        }

        InsertComponent(conn, tx, snapshotId, nodeId, "CPU", hw.Cpu.Name, null,
            $"{hw.Cpu.Cores}C/{hw.Cpu.Threads}T @ {hw.Cpu.MaxClockGhz}GHz", now);

        foreach (var ram in hw.RamModules)
            InsertComponent(conn, tx, snapshotId, nodeId, "RAM", ram.Manufacturer, null,
                $"{ram.Type} {ram.SizeBytes / 1024 / 1024 / 1024}GB @ {ram.SpeedMhz}MHz", now);

        foreach (var disk in hw.Disks)
            InsertComponent(conn, tx, snapshotId, nodeId, "Disk", disk.Model, null,
                $"{disk.SizeBytes / 1024 / 1024 / 1024}GB [{disk.InterfaceType}, {disk.MediaType}]", now);

        foreach (var gpu in hw.Gpus)
            InsertComponent(conn, tx, snapshotId, nodeId, "GPU", gpu.Name, null,
                $"VRAM: {(gpu.VramBytes ?? 0) / 1024 / 1024 / 1024}GB, Driver: {gpu.DriverVersion}", now);

        InsertComponent(conn, tx, snapshotId, nodeId, "Motherboard", hw.Motherboard.Product,
            hw.Motherboard.SerialNumber, hw.Motherboard.Manufacturer, now);

        Exec(conn, tx, @"
            INSERT INTO OsInfo (SnapshotId, NodeId, Caption, Version, Architecture, CapturedAt)
            VALUES ($sid, $nid, $cap, $ver, $arch, $capAt);",
            ("$sid", snapshotId), ("$nid", nodeId), ("$cap", hw.Os.Caption),
            ("$ver", hw.Os.Version), ("$arch", hw.Os.Architecture),
            ("$capAt", now.ToString(DateFmt)));

        tx.Commit();
    }

    private static void InsertComponent(SqliteConnection conn, SqliteTransaction tx,
        int snapshotId, string nodeId, string category, string? name, string? serial,
        string? specs, DateTime capturedAt)
    {
        Exec(conn, tx, @"
            INSERT INTO Components (SnapshotId, NodeId, Category, Name, Vendor, SerialNumber, Specs, CapturedAt)
            VALUES ($sid, $nid, $cat, $name, $vendor, $serial, $specs, $capAt);",
            ("$sid", snapshotId), ("$nid", nodeId), ("$cat", category),
            ("$name", name ?? "Unknown"), ("$vendor", (object?)null), ("$serial", (object?)serial),
            ("$specs", specs), ("$capAt", capturedAt.ToString(DateFmt)));
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<AgentRecord> GetAllAgents()
    {
        var result = new List<AgentRecord>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT n.NodeId, n.DomainName, n.IpAddress, n.Platform, n.OsVersion, n.LastSeen,
                   (SELECT COUNT(*) FROM Snapshots s WHERE s.NodeId = n.NodeId) as SnapshotCount
            FROM Nodes n
            ORDER BY n.LastSeen DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AgentRecord
            {
                NodeId = reader.GetString(0),
                DomainName = reader.GetString(1),
                IpAddress = reader.IsDBNull(2) ? null : reader.GetString(2),
                Platform = reader.IsDBNull(3) ? null : reader.GetString(3),
                OsVersion = reader.IsDBNull(4) ? null : reader.GetString(4),
                LastSeen = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                SnapshotCount = reader.GetInt32(6)
            });
        }
        return result;
    }

    public List<ComponentItem> GetLatestComponents(string nodeId)
    {
        var result = new List<ComponentItem>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();


        cmd.CommandText = @"
            SELECT Category, Name, Specs FROM Components
            WHERE SnapshotId = (
                SELECT SnapshotId FROM Snapshots 
                WHERE NodeId = $nid 
                ORDER BY SnapshotId DESC LIMIT 1
            )
            ORDER BY Category, Name;";

        cmd.Parameters.AddWithValue("$nid", nodeId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ComponentItem
            {
                Category = reader.GetString(0),
                Name = reader.GetString(1),
                Specs = reader.IsDBNull(2) ? "" : reader.GetString(2)
            });
        }
        return result;
    }
    public void ClearAllData()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();

       
        cmd.CommandText = "DELETE FROM Nodes;";
        cmd.ExecuteNonQuery();
    }
}