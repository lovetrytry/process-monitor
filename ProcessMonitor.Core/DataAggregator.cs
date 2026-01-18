using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProcessMonitor.Core.Models;

namespace ProcessMonitor.Core
{
    public class DataAggregator
    {
        private readonly ProcessCollector _collector;
        private readonly System.Timers.Timer _timer;
        private readonly object _lock = new object();
        
        // PID -> List of 1-second metrics
        private Dictionary<int, List<ProcessMetric>> _buffer = new();
        
        public event EventHandler<AggregatedReport>? ReportReady;

        public DataAggregator()
        {
            _collector = new ProcessCollector();
            _timer = new System.Timers.Timer(1000); // 1 second
            _timer.Elapsed += (s, e) => CollectSample();
            _timer.AutoReset = true;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void CollectSample()
        {
            try
            {
                var samples = _collector.Collect();
                lock (_lock)
                {
                    foreach (var sample in samples)
                    {
                        if (!_buffer.ContainsKey(sample.Id))
                        {
                            _buffer[sample.Id] = new List<ProcessMetric>();
                        }
                        _buffer[sample.Id].Add(sample);
                    }

                    // Check if we have 60 seconds worth of data (roughly)
                    // We check the first process in buffer. If it has 60 samples, we trigger aggregation.
                    // Or simpler: just track ticks.
                }
                
                _tickCount++;
                if (_tickCount >= 60)
                {
                    AggregateAndReport();
                    _tickCount = 0;
                }
            }
            catch (Exception ex)
            {
                // Log error
                System.Diagnostics.Debug.WriteLine($"Collection Error: {ex.Message}");
            }
        }

        private int _tickCount = 0;

        private void AggregateAndReport()
        {
            Dictionary<int, List<ProcessMetric>> bufferSnapshot;
            lock (_lock)
            {
                bufferSnapshot = new Dictionary<int, List<ProcessMetric>>(_buffer);
                _buffer.Clear();
            }

            Task.Run(() => 
            {
                // 1. Convert Buffer to List of AggregatedProcessItem (Per PID)
                var rawItems = new List<AggregatedProcessItem>();
                foreach (var kvp in bufferSnapshot)
                {
                    var pid = kvp.Key;
                    var metrics = kvp.Value;
                    if (metrics.Count == 0) continue;

                    var first = metrics.First();
                    var last = metrics.Last();

                    var item = new AggregatedProcessItem
                    {
                        Id = pid, 
                        Name = first.Name,
                        Path = first.Path,
                        
                        CpuUsagePercentAvg = metrics.Average(m => m.CpuUsagePercent),
                        CpuTimeTotalMs = (last.TotalProcessorTime - first.TotalProcessorTime).TotalMilliseconds,
                        MemoryUsageBytesAvg = (long)metrics.Average(m => m.MemoryUsageBytes),
                        DiskReadBytesTotal = last.DiskReadBytes - first.DiskReadBytes,
                        DiskWriteBytesTotal = last.DiskWriteBytes - first.DiskWriteBytes
                    };
                    
                    if (item.DiskReadBytesTotal < 0) item.DiskReadBytesTotal = 0;
                    if (item.DiskWriteBytesTotal < 0) item.DiskWriteBytesTotal = 0;

                    rawItems.Add(item);
                }

                // 2. Group by Name & Path (Merge same processes)
                var mergedItems = rawItems
                    .GroupBy(x => new { x.Name, x.Path })
                    .Select(g => new AggregatedProcessItem
                    {
                        Id = 0, // Merged
                        Name = g.Key.Name,
                        Path = g.Key.Path,
                        CpuUsagePercentAvg = g.Sum(x => x.CpuUsagePercentAvg),
                        CpuTimeTotalMs = g.Sum(x => x.CpuTimeTotalMs),
                        MemoryUsageBytesAvg = g.Sum(x => x.MemoryUsageBytesAvg),
                        DiskReadBytesTotal = g.Sum(x => x.DiskReadBytesTotal),
                        DiskWriteBytesTotal = g.Sum(x => x.DiskWriteBytesTotal)
                    })
                    .ToList();

                // 3. Calculate Global Ranks (Before filtering)
                // CPU Ranks
                var cpuSorted = mergedItems.OrderByDescending(x => x.CpuUsagePercentAvg).ToList();
                for (int i = 0; i < cpuSorted.Count; i++) cpuSorted[i].CpuGlobalRank = i + 1;

                // Memory Ranks
                var memSorted = mergedItems.OrderByDescending(x => x.MemoryUsageBytesAvg).ToList();
                for (int i = 0; i < memSorted.Count; i++) memSorted[i].MemGlobalRank = i + 1;

                // IO Ranks
                var ioSorted = mergedItems.OrderByDescending(x => x.DiskTotalBytes).ToList();
                for (int i = 0; i < ioSorted.Count; i++) ioSorted[i].IoGlobalRank = i + 1;

                // 4. Smart Selection: Union of Top 20 in each category
                // User observed that strict 20 limit (even with Round-Robin) is "unreasonable" because it truncates 
                // legitimate Top 20 candidates (e.g. IO Rank #8) to make room for others.
                // To guarantee "Top 20" means "Top 20", we must store the Union of (Top 20 CPU + Top 20 Mem + Top 20 IO).
                // This means items count will be between 20 and 60 per minute.
                // With sharding, this manageable.
                
                var topCpu = mergedItems.OrderByDescending(x => x.CpuUsagePercentAvg).Take(20);
                var topMem = mergedItems.OrderByDescending(x => x.MemoryUsageBytesAvg).Take(20);
                var topIo = mergedItems.OrderByDescending(x => x.DiskTotalBytes).Take(20);

                var finalSet = new HashSet<AggregatedProcessItem>();
                foreach (var item in topCpu) finalSet.Add(item);
                foreach (var item in topMem) finalSet.Add(item);
                foreach (var item in topIo) finalSet.Add(item);

                var report = new AggregatedReport
                {
                    Timestamp = DateTime.Now,
                    Items = finalSet.ToList()
                };

                ReportReady?.Invoke(this, report);
            });
        }
    }

    public class AggregatedReport
    {
        public DateTime Timestamp { get; set; }
        public List<AggregatedProcessItem> Items { get; set; } = new List<AggregatedProcessItem>();
    }

    public class AggregatedProcessItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        public double CpuUsagePercentAvg { get; set; }
        public double CpuTimeTotalMs { get; set; }
        
        public long MemoryUsageBytesAvg { get; set; }
        
        public long DiskReadBytesTotal { get; set; }
        public long DiskWriteBytesTotal { get; set; }
        public long DiskTotalBytes => DiskReadBytesTotal + DiskWriteBytesTotal;

        // Ranks for persistent storage logic
        public int CpuGlobalRank { get; set; }
        public int MemGlobalRank { get; set; }
        public int IoGlobalRank { get; set; }
    }
}
