using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProcessMonitor.Core.Models;

namespace ProcessMonitor.Core
{
    public class ProcessCollector
    {
        private class ProcessState
        {
            public TimeSpan TotalProcessorTime { get; set; }
            public DateTime SnapshotTime { get; set; }
            public long DiskReadBytes { get; set; }
            public long DiskWriteBytes { get; set; }
        }

        // P/Invoke for Disk IO
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetProcessIoCounters(IntPtr ProcessHandle, out IO_COUNTERS IoCounters);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        private readonly Dictionary<int, ProcessState> _previousStates = new();
        private readonly int _processorCount;

        public ProcessCollector()
        {
            _processorCount = Environment.ProcessorCount;
        }

        /// <summary>
        /// Captures current process metrics. Should be called periodically (e.g., every second).
        /// </summary>
        public List<ProcessMetric> Collect()
        {
            var metrics = new List<ProcessMetric>();
            var processes = Process.GetProcesses();
            var now = DateTime.Now;

            // Update state and remove dead processes from cache
            var currentPids = new HashSet<int>();

            foreach (var p in processes)
            {
                currentPids.Add(p.Id);
                if (p.Id == 0) continue; // Skip System Idle Process

                try
                {
                    // Basic Info
                    var metric = new ProcessMetric
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Timestamp = now,
                        MemoryUsageBytes = p.WorkingSet64,
                        // MemoryUsagePercent requires TotalMemory, can be calculated later
                    };

                    // Path (Requires permission)
                    try 
                    { 
                        // MainModule can throw for system processes or 32/64 bit mismatch
                        if (!HasExited(p))
                        {
                            metric.Path = p.MainModule?.FileName ?? string.Empty;
                        }
                    }
                    catch { metric.Path = "<Access Denied>"; }

                    // Attempt to get previous state once
                    _previousStates.TryGetValue(p.Id, out var prevState);

                    // Disk IO via P/Invoke
                    try
                    {
                        if (GetProcessIoCounters(p.Handle, out var ioCounters))
                        {
                            long currentRead = (long)ioCounters.ReadTransferCount;
                            long currentWrite = (long)ioCounters.WriteTransferCount;

                            metric.DiskReadBytes = currentRead;
                            metric.DiskWriteBytes = currentWrite;

                            if (prevState != null)
                            {
                                var timeDiffSec = (now - prevState.SnapshotTime).TotalSeconds;
                                if (timeDiffSec > 0)
                                {
                                    var readDiff = currentRead - prevState.DiskReadBytes;
                                    var writeDiff = currentWrite - prevState.DiskWriteBytes;
                                    // Ensure non-negative
                                    if (readDiff < 0) readDiff = 0;
                                    if (writeDiff < 0) writeDiff = 0;

                                    metric.DiskUsageRateBytesPerSec = (long)((readDiff + writeDiff) / timeDiffSec);
                                }
                            }
                            
                            // Live update of existing state for disk bytes?
                            // We shouldn't mutate `prevState` yet if we want consistent snapshot diff for CPU later?
                            // Actually it's fine.
                        }
                    }
                    catch { /* Handle/Disk access denied */ }

                    // CPU Calculation
                    var currentTotalProcessorTime = p.TotalProcessorTime;
                    
                    if (prevState != null)
                    {
                        var timeDiff = (now - prevState.SnapshotTime).TotalMilliseconds;
                        var cpuDiff = (currentTotalProcessorTime - prevState.TotalProcessorTime).TotalMilliseconds;
                        
                        if (timeDiff > 0)
                        {
                            // CPU Usage = (ProcessTimeDelta / WallTimeDelta) / ProcessorCount * 100
                            metric.CpuUsagePercent = (cpuDiff / timeDiff) / _processorCount * 100;
                        }

                        // Sanity check
                        if (metric.CpuUsagePercent < 0) metric.CpuUsagePercent = 0;
                        if (metric.CpuUsagePercent > 100) metric.CpuUsagePercent = 100;

                        // Calculate derived totals if needed or just snapshot
                        metric.TotalProcessorTime = currentTotalProcessorTime;
                    }
                    else
                    {
                        // First time seeing this process, can't calculate rate yet
                        metric.CpuUsagePercent = 0;
                        metric.TotalProcessorTime = currentTotalProcessorTime;
                    }

                    // Update state
                    if (!_previousStates.ContainsKey(p.Id))
                    {
                        _previousStates[p.Id] = new ProcessState();
                    }
                    
                    var state = _previousStates[p.Id];
                    state.TotalProcessorTime = currentTotalProcessorTime;
                    state.SnapshotTime = now;
                    // Disk bytes should have been set above if available, if not, keep 0
                    if (metric.DiskReadBytes > 0 || metric.DiskWriteBytes > 0)
                    {
                        state.DiskReadBytes = metric.DiskReadBytes;
                        state.DiskWriteBytes = metric.DiskWriteBytes;
                    }

                    metrics.Add(metric);
                }
                catch (Exception)
                {
                    // Process likely exited or access denied
                }
            }

            // Cleanup dead processes from cache
            var deadPids = _previousStates.Keys.Where(k => !currentPids.Contains(k)).ToList();
            foreach (var pid in deadPids)
            {
                _previousStates.Remove(pid);
            }

            return metrics;
        }

        private bool HasExited(Process p)
        {
            try { return p.HasExited; }
            catch { return true; }
        }
    }
}
