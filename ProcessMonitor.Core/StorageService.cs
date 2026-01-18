using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using ProcessMonitor.Core.Models;

namespace ProcessMonitor.Core
{
    public class StorageService
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public StorageService(string dbName = "metrics.db")
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(appData, "ProcessMonitor");
            Directory.CreateDirectory(folder);
            _dbPath = Path.Combine(folder, dbName);
            _connectionString = $"Data Source={_dbPath}";
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS ProcessMetrics (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Pid INTEGER,
                    Name TEXT,
                    Path TEXT,
                    CpuUsagePercent REAL,
                    CpuTimeTotalMs REAL,
                    MemoryUsageBytes INTEGER,
                    DiskReadBytes INTEGER,
                    DiskWriteBytes INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_timestamp ON ProcessMetrics(Timestamp);

                CREATE TABLE IF NOT EXISTS DailyProcessStats (
                    Date TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    CpuCount INTEGER DEFAULT 0,
                    MemoryCount INTEGER DEFAULT 0,
                    IoCount INTEGER DEFAULT 0,
                    PRIMARY KEY (Date, Path)
                );
            ";
            command.ExecuteNonQuery();
        }

        public void RebuildStatsAsync()
        {
            // Run on background thread to avoid blocking UI
            // Also wrap in try-catch to prevent crashing
            try 
            {
                using var connection = new SqliteConnection(_connectionString);
                connection.Open();

                // Check if DailyProcessStats is empty
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM DailyProcessStats";
                long count = (long)cmd.ExecuteScalar();
                if (count > 0) return; // Already populated

                // Get all sharded tables
                var tables = new List<string>();
                var listCmd = connection.CreateCommand();
                listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'ProcessMetrics_%'";
                using (var reader = listCmd.ExecuteReader())
                {
                    while (reader.Read()) tables.Add(reader.GetString(0));
                }

                // Also check legacy ProcessMetrics if exists
                var checkLegacy = connection.CreateCommand();
                checkLegacy.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ProcessMetrics'";
                if (checkLegacy.ExecuteScalar() != null) tables.Add("ProcessMetrics");

                foreach (var table in tables)
                {
                    using var transaction = connection.BeginTransaction();
                    var rebuildCmd = connection.CreateCommand();
                    rebuildCmd.Transaction = transaction;
                    rebuildCmd.CommandText = $@"
                        INSERT INTO DailyProcessStats (Date, Name, Path, CpuCount, MemoryCount, IoCount)
                        SELECT 
                            substr(Timestamp, 1, 10) as DateStr,
                            MAX(Name),
                            Path,
                            SUM(CASE WHEN CpuRank <= 20 THEN 1 ELSE 0 END) as CpuCount,
                            SUM(CASE WHEN MemRank <= 20 THEN 1 ELSE 0 END) as MemCount,
                            SUM(CASE WHEN IoRank <= 20 THEN 1 ELSE 0 END) as IoCount
                        FROM (
                            SELECT 
                                Timestamp, Name, Path,
                                RANK() OVER (PARTITION BY Timestamp ORDER BY CpuUsagePercent DESC) as CpuRank,
                                RANK() OVER (PARTITION BY Timestamp ORDER BY MemoryUsageBytes DESC) as MemRank,
                                RANK() OVER (PARTITION BY Timestamp ORDER BY (DiskReadBytes + DiskWriteBytes) DESC) as IoRank
                            FROM {table}
                        )
                        WHERE CpuRank <= 20 OR MemRank <= 20 OR IoRank <= 20
                        GROUP BY DateStr, Path
                        ON CONFLICT(Date, Path) DO UPDATE SET
                            CpuCount = CpuCount + excluded.CpuCount,
                            MemoryCount = MemoryCount + excluded.MemoryCount,
                            IoCount = IoCount + excluded.IoCount
                    ";
                    rebuildCmd.ExecuteNonQuery();
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                 System.IO.File.AppendAllText("db_error.log", $"{DateTime.Now}: Rebuild failed: {ex}\n");
            }
        }


        public void SaveReport(AggregatedReport report)
        {
            if (report == null || report.Items == null || report.Items.Count == 0) return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            // 1. Determine dynamic table name: ProcessMetrics_YYYYMM
            string tableName = $"ProcessMetrics_{report.Timestamp:yyyyMM}";
            
            // 2. Ensure table exists (optimistically create)
            var createCmd = connection.CreateCommand();
            createCmd.Transaction = transaction;
            createCmd.CommandText = $@"
                CREATE TABLE IF NOT EXISTS {tableName} (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp TEXT NOT NULL,
                    Pid INTEGER,
                    Name TEXT,
                    Path TEXT,
                    CpuUsagePercent REAL,
                    CpuTimeTotalMs REAL,
                    MemoryUsageBytes INTEGER,
                    DiskReadBytes INTEGER,
                    DiskWriteBytes INTEGER
                );
                CREATE INDEX IF NOT EXISTS idx_{tableName}_timestamp ON {tableName}(Timestamp);
            ";
            createCmd.ExecuteNonQuery();

            // 3. Insert Data
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
                INSERT INTO {tableName} 
                (Timestamp, Pid, Name, Path, CpuUsagePercent, CpuTimeTotalMs, MemoryUsageBytes, DiskReadBytes, DiskWriteBytes)
                VALUES 
                ($ts, $pid, $name, $path, $cpu, $cputime, $mem, $diskread, $diskwrite)
            ";

            var pTs = command.CreateParameter(); pTs.ParameterName = "$ts"; command.Parameters.Add(pTs);
            var pPid = command.CreateParameter(); pPid.ParameterName = "$pid"; command.Parameters.Add(pPid);
            var pName = command.CreateParameter(); pName.ParameterName = "$name"; command.Parameters.Add(pName);
            var pPath = command.CreateParameter(); pPath.ParameterName = "$path"; command.Parameters.Add(pPath);
            var pCpu = command.CreateParameter(); pCpu.ParameterName = "$cpu"; command.Parameters.Add(pCpu);
            var pCpuTime = command.CreateParameter(); pCpuTime.ParameterName = "$cputime"; command.Parameters.Add(pCpuTime);
            var pMem = command.CreateParameter(); pMem.ParameterName = "$mem"; command.Parameters.Add(pMem);
            var pDRead = command.CreateParameter(); pDRead.ParameterName = "$diskread"; command.Parameters.Add(pDRead);
            var pDWrite = command.CreateParameter(); pDWrite.ParameterName = "$diskwrite"; command.Parameters.Add(pDWrite);

            string tsStr = report.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            string dateStr = report.Timestamp.ToString("yyyy-MM-dd");

            // Daily Stats Update (unchanged table, but logic updated for merged items)
            // Note: DataAggregator already merged items, so 'Id' (PID) is 0. 
            // We still track Stats for Rankings same as before.
            
            // Identify Top 20s (Since input is ALREADY Top 20, they are ALL Top 20/All Candidates)
            // But we should still respect the categories if we want separate counters.
            // Since the list Is Top 20 CPU/Mem sorted, likely all of them are candidates.
            // We can just count them all? Or still try to see if they distinguish?
            // User requirement: "Store Top 20". We are storing Top 20.
            // Rankings logic tracks counts.
            // Let's just treat all entries as "Top" for simplicity, or re-rank to distinguish Cpu vs Mem vs IO specific tops?
            // With only 20 items, simpler to say they all contribute to the "Total" rank?
            // But we split Cpu/Mem/Io counts.
            // Let's re-calculate local Top 20 logic within this set of 20 (it will just mark all of them if they are non-zero).
            
            var cpuTop20 = report.Items.OrderByDescending(x => x.CpuUsagePercentAvg).Take(20).Select(x => x.Name).ToHashSet();
            var memTop20 = report.Items.OrderByDescending(x => x.MemoryUsageBytesAvg).Take(20).Select(x => x.Name).ToHashSet();
            var ioTop20 = report.Items.OrderByDescending(x => x.DiskReadBytesTotal + x.DiskWriteBytesTotal).Take(20).Select(x => x.Name).ToHashSet();

            // Prepare stats command
            var statsCommand = connection.CreateCommand();
            statsCommand.Transaction = transaction;
            statsCommand.CommandText = @"
                INSERT INTO DailyProcessStats (Date, Name, Path, CpuCount, MemoryCount, IoCount)
                VALUES ($date, $name, $path, $cpuInc, $memInc, $ioInc)
                ON CONFLICT(Date, Path) DO UPDATE SET 
                    CpuCount = CpuCount + $cpuInc,
                    MemoryCount = MemoryCount + $memInc,
                    IoCount = IoCount + $ioInc;
            ";
            var pStatsDate = statsCommand.CreateParameter(); pStatsDate.ParameterName = "$date"; statsCommand.Parameters.Add(pStatsDate);
            var pStatsName = statsCommand.CreateParameter(); pStatsName.ParameterName = "$name"; statsCommand.Parameters.Add(pStatsName);
            var pStatsPath = statsCommand.CreateParameter(); pStatsPath.ParameterName = "$path"; statsCommand.Parameters.Add(pStatsPath);
            var pCpuInc = statsCommand.CreateParameter(); pCpuInc.ParameterName = "$cpuInc"; statsCommand.Parameters.Add(pCpuInc);
            var pMemInc = statsCommand.CreateParameter(); pMemInc.ParameterName = "$memInc"; statsCommand.Parameters.Add(pMemInc);
            var pIoInc = statsCommand.CreateParameter(); pIoInc.ParameterName = "$ioInc"; statsCommand.Parameters.Add(pIoInc);

            foreach (var item in report.Items)
            {
                pTs.Value = tsStr;
                pPid.Value = item.Id; // Likely 0
                pName.Value = item.Name ?? "";
                pPath.Value = item.Path ?? "";
                pCpu.Value = item.CpuUsagePercentAvg;
                pCpuTime.Value = item.CpuTimeTotalMs;
                pMem.Value = item.MemoryUsageBytesAvg;
                pDRead.Value = item.DiskReadBytesTotal;
                pDWrite.Value = item.DiskWriteBytesTotal;

                command.ExecuteNonQuery();

                // Update Stats
                bool isCpu = cpuTop20.Contains(item.Name);
                bool isMem = memTop20.Contains(item.Name);
                bool isIo = ioTop20.Contains(item.Name);

                if ((isCpu || isMem || isIo) && !string.IsNullOrEmpty(item.Path))
                {
                    pStatsDate.Value = dateStr;
                    pStatsName.Value = item.Name ?? "";
                    pStatsPath.Value = item.Path;
                    pCpuInc.Value = isCpu ? 1 : 0;
                    pMemInc.Value = isMem ? 1 : 0;
                    pIoInc.Value = isIo ? 1 : 0;

                    statsCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }

        public void CleanHistory(DateTime cutoff)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            
            // Get all tables
            var tables = new List<string>();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name LIKE 'ProcessMetrics_%'";
            using (var reader = cmd.ExecuteReader())
            {
                while(reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            // Drop old tables
            var cutoffInt = int.Parse(cutoff.ToString("yyyyMM"));
            foreach (var table in tables)
            {
                var suffix = table.Replace("ProcessMetrics_", "");
                if (int.TryParse(suffix, out int tableDate))
                {
                    if (tableDate < cutoffInt)
                    {
                        var dropCmd = connection.CreateCommand();
                        dropCmd.CommandText = $"DROP TABLE IF EXISTS {table}";
                        dropCmd.ExecuteNonQuery();
                    }
                }
            }
            
            // Should also clean DailyProcessStats? 
            // Previous CleanHistory logic: DELETE FROM ProcessMetrics ...
            // We should cleanup DailyProcessStats too.
            var cleanStats = connection.CreateCommand();
            cleanStats.CommandText = "DELETE FROM DailyProcessStats WHERE Date < $cut";
            cleanStats.Parameters.AddWithValue("$cut", cutoff.ToString("yyyy-MM-dd"));
            cleanStats.ExecuteNonQuery();

            // Vacuum
            var vacuumCmd = connection.CreateCommand();
            vacuumCmd.CommandText = "VACUUM";
            vacuumCmd.ExecuteNonQuery();
        }

        public List<string> GetAvailableTimestamps(DateTime date, int? hour = null)
        {
            var list = new List<string>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string tableName = $"ProcessMetrics_{date:yyyyMM}";
            
            // Check if table exists
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $tb";
            checkCmd.Parameters.AddWithValue("$tb", tableName);
            if (checkCmd.ExecuteScalar() == null) return list;

            var command = connection.CreateCommand();
            string pattern;
            if (hour.HasValue)
                pattern = $"{date:yyyy-MM-dd} {hour.Value:00}%";
            else
                pattern = $"{date:yyyy-MM-dd}%";

            command.CommandText = $"SELECT DISTINCT Timestamp FROM {tableName} WHERE Timestamp LIKE $datePattern ORDER BY Timestamp DESC";
            command.Parameters.AddWithValue("$datePattern", pattern);
            
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(reader.GetString(0));
            }
            return list;
        }

        public List<string> GetAvailableTimestamps()
        {
            return GetAvailableTimestamps(DateTime.Today);
        }

        public List<AggregatedProcessItem> GetReportAt(string timestamp)
        {
            var list = new List<AggregatedProcessItem>();
            if (!DateTime.TryParse(timestamp, out DateTime dt)) return list;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            string tableName = $"ProcessMetrics_{dt:yyyyMM}";

            // Check if table exists
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $tb";
            checkCmd.Parameters.AddWithValue("$tb", tableName);
            if (checkCmd.ExecuteScalar() == null) return list;

            var command = connection.CreateCommand();
            command.CommandText = $@"
                SELECT Pid, Name, Path, CpuUsagePercent, CpuTimeTotalMs, MemoryUsageBytes, DiskReadBytes, DiskWriteBytes 
                FROM {tableName}
                WHERE Timestamp = $ts";
            command.Parameters.AddWithValue("$ts", timestamp);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new AggregatedProcessItem
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Path = reader.GetString(2),
                    CpuUsagePercentAvg = reader.GetDouble(3),
                    CpuTimeTotalMs = reader.GetDouble(4),
                    MemoryUsageBytesAvg = reader.GetInt64(5),
                    DiskReadBytesTotal = reader.GetInt64(6),
                    DiskWriteBytesTotal = reader.GetInt64(7)
                });
            }
            return list;
        }
        public void ExportHistoryToCsv(DateTime startDate, DateTime endDate, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            // Write Header
            writer.WriteLine("Timestamp,PID,Name,Path,CpuUsagePercent,CpuTimeTotalMs,MemoryUsageBytes,DiskReadBytes,DiskWriteBytes");

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            // Iterate through every month in the range
            var current = new DateTime(startDate.Year, startDate.Month, 1);
            var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

            while (current <= endMonth)
            {
                string tableName = $"ProcessMetrics_{current:yyyyMM}";
                
                // Check exist
                var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name = $tb";
                checkCmd.Parameters.AddWithValue("$tb", tableName);
                
                if (checkCmd.ExecuteScalar() != null)
                {
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = $@"
                        SELECT Timestamp, Pid, Name, Path, CpuUsagePercent, CpuTimeTotalMs, MemoryUsageBytes, DiskReadBytes, DiskWriteBytes
                        FROM {tableName}
                        WHERE Timestamp >= $start AND Timestamp < $end
                        ORDER BY Timestamp ASC";
                    
                    // Adjust query range for this specific table (Month boundaries vs User range)
                    // The SQL query handles the exact timestamp comparison, so passing user's full start/end is safe 
                    // IF we are sure we want to query globally. 
                    // Actually, passing strict Start/End to SQL is safest.
                    cmd.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd 00:00:00"));
                    // Make sure we include the end date fully (until 23:59:59 or next day 00:00)
                    cmd.Parameters.AddWithValue("$end", endDate.AddDays(1).ToString("yyyy-MM-dd 00:00:00"));

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var line = string.Join(",", 
                            reader.GetString(0), // Timestamp
                            reader.GetInt32(1), // PID
                            EscapeCsv(reader.GetString(2)), // Name
                            EscapeCsv(reader.IsDBNull(3) ? "" : reader.GetString(3)), // Path
                            reader.GetDouble(4), // CPU
                            reader.GetDouble(5), // CPUTime
                            reader.GetInt64(6), // Mem
                            reader.GetInt64(7), // Read
                            reader.GetInt64(8)  // Write
                        );
                        writer.WriteLine(line);
                    }
                }

                current = current.AddMonths(1);
            }
        }

        private string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        public List<RankedProcessItem> GetRankings(DateTime startDate, DateTime endDate)
        {
            var list = new List<RankedProcessItem>();
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Name, Path, 
                       SUM(CpuCount) as CpuTotal,
                       SUM(MemoryCount) as MemTotal,
                       SUM(IoCount) as IoTotal
                FROM DailyProcessStats 
                WHERE Date >= $start AND Date <= $end 
                GROUP BY Name, Path 
                ORDER BY (CpuTotal + MemTotal + IoTotal) DESC 
                LIMIT 100";
            
            command.Parameters.AddWithValue("$start", startDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$end", endDate.ToString("yyyy-MM-dd"));

            using var reader = command.ExecuteReader();
            int rank = 1;
            while (reader.Read())
            {
                var cpu = reader.GetInt32(2);
                var mem = reader.GetInt32(3);
                var io = reader.GetInt32(4);

                list.Add(new RankedProcessItem
                {
                    Rank = rank++,
                    Name = reader.GetString(0),
                    Path = reader.GetString(1),
                    CpuCount = cpu,
                    MemoryCount = mem,
                    IoCount = io,
                    TotalCount = cpu + mem + io
                });
            }
            return list;
        }
    }

    public class RankedProcessItem
    {
        public int Rank { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public int CpuCount { get; set; }
        public int MemoryCount { get; set; }
        public int IoCount { get; set; }
    }
}
