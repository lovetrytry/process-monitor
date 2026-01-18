using System;

namespace ProcessMonitor.Core.Models
{
    public class ProcessMetric
    {
        public int Id { get; set; } // Process Id
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        
        // CPU
        public double CpuUsagePercent { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }

        // Memory
        public long MemoryUsageBytes { get; set; } // Working Set
        public double MemoryUsagePercent { get; set; } // Working Set / Total Memory

        // Disk (IO)
        public long DiskReadBytes { get; set; }
        public long DiskWriteBytes { get; set; }
        public long DiskTotalBytes => DiskReadBytes + DiskWriteBytes;
        public long DiskUsageRateBytesPerSec { get; set; } // Derived rate

        // Network (Placeholder for now, requires ETW/Admin)
        public long NetworkSentBytes { get; set; }
        public long NetworkReceivedBytes { get; set; }
        public long NetworkTotalBytes => NetworkSentBytes + NetworkReceivedBytes;

        // GPU (Placeholder)
        public double GpuUsagePercent { get; set; }

        public DateTime Timestamp { get; set; }
    }
}
