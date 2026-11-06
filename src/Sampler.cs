// Gemeinsamer Messkern für yoga.exe und YogaPanel.exe.
using System;
using System.Collections.Generic;
using System.Linq;

namespace YogaTool
{
    public class ProcRow { public string Name; public int Count; public double Cpu, Gpu, MemMB, Watt; }

    public class Sampler : IDisposable
    {
        Pdh pdh = new Pdh(), pdhGpu = new Pdh(); // GPU-Zähler getrennt: teuer, nur im vollen Modus
        // false = nur Leistung/CPU/Temperatur messen (z. B. wenn das Fenster versteckt ist)
        public bool Full = true;
        Dictionary<int, ProcSample> last;
        DateTime lastT;
        public double CpuUtil, GHz, TempMax, GpuTotal;
        public double MemTotalGB, MemUsedGB, CommitGB, CommitLimitGB, PageFilePct, PagesPerSec;
        public Dictionary<string, double> Rapl = new Dictionary<string, double>();
        public List<ProcRow> Rows = new List<ProcRow>();

        public Sampler()
        {
            pdh.Add("util", @"\Processor Information(_Total)\% Processor Utility");
            pdh.Add("freq", @"\Processor Information(_Total)\Processor Frequency");
            pdh.Add("perf", @"\Processor Information(_Total)\% Processor Performance");
            pdh.Add("rapl", @"\Energy Meter(*)\Power");
            pdhGpu.Add("gpu", @"\GPU Engine(*)\Utilization Percentage");
            pdh.Add("temp", @"\Thermal Zone Information(*)\High Precision Temperature");
            pdh.Add("pf", @"\Paging File(_Total)\% Usage");
            pdh.Add("pages", @"\Memory\Pages/sec"); // Seiten, die wegen harter Seitenfehler von/auf Datenträger gehen
            pdh.Collect(); pdhGpu.Collect(); last = Procs.Snapshot(); lastT = DateTime.UtcNow;
        }

        public void Sample()
        {
            pdh.Collect();
            CpuUtil = Math.Min(100, pdh.Value("util"));
            GHz = pdh.Value("freq") * pdh.Value("perf") / 100 / 1000;
            var temps = pdh.Array("temp");
            TempMax = temps.Count > 0 ? temps.Values.Max() / 10 - 273.15 : double.NaN;
            Rapl = pdh.Array("rapl");
            Mem.Read(out MemTotalGB, out MemUsedGB, out CommitGB, out CommitLimitGB);
            PageFilePct = pdh.Value("pf");
            PagesPerSec = pdh.Value("pages");
            if (!Full) return;
            pdhGpu.Collect();
            var now = Procs.Snapshot(); var t = DateTime.UtcNow;
            double span = (t - lastT).Ticks * (double)Environment.ProcessorCount;

            // GPU pro PID: je Engine-Typ summieren, dann Maximum (wie Task-Manager)
            var perPidType = new Dictionary<int, Dictionary<string, double>>();
            var perType = new Dictionary<string, double>();
            foreach (var kv in pdhGpu.Array("gpu"))
            {
                string n = kv.Key; int pi = n.IndexOf("pid_"), ti = n.IndexOf("engtype_");
                if (pi < 0 || ti < 0) continue;
                int pe = n.IndexOf('_', pi + 4); int pid;
                if (pe < 0 || !int.TryParse(n.Substring(pi + 4, pe - pi - 4), out pid)) continue;
                string type = n.Substring(ti + 8);
                Dictionary<string, double> d;
                if (!perPidType.TryGetValue(pid, out d)) perPidType[pid] = d = new Dictionary<string, double>();
                double cur; d.TryGetValue(type, out cur); d[type] = cur + kv.Value;
                perType.TryGetValue(type, out cur); perType[type] = cur + kv.Value;
            }
            GpuTotal = perType.Count > 0 ? Math.Min(100, perType.Values.Max()) : 0;

            var groups = new Dictionary<string, ProcRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in now.Values)
            {
                if (p.Pid == 0) continue;
                ProcSample old;
                double cpu = last.TryGetValue(p.Pid, out old) && old.Name == p.Name && span > 0 ? Math.Max(0, (p.CpuTime - old.CpuTime) / span * 100) : 0;
                Dictionary<string, double> gd;
                double gpu = perPidType.TryGetValue(p.Pid, out gd) ? Math.Min(100, gd.Values.Max()) : 0;
                string name = p.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p.Name.Substring(0, p.Name.Length - 4) : p.Name;
                ProcRow r;
                if (!groups.TryGetValue(name, out r)) groups[name] = r = new ProcRow { Name = name };
                r.Count++; r.Cpu += cpu; r.Gpu += gpu; r.MemMB += p.PrivateBytes / 1048576.0;
            }
            // Grobe Watt-Zuordnung: Kern-Leistung nach CPU-Anteil, GPU-Leistung nach GPU-Anteil
            double pp0, pp1, sumCpu = groups.Values.Sum(x => x.Cpu), sumGpu = groups.Values.Sum(x => x.Gpu);
            Rapl.TryGetValue("RAPL_Package0_PP0", out pp0); Rapl.TryGetValue("RAPL_Package0_PP1", out pp1);
            foreach (var r in groups.Values)
            {
                r.Gpu = Math.Min(100, r.Gpu);
                r.Watt = (sumCpu > 0 ? pp0 / 1000 * r.Cpu / sumCpu : 0) + (sumGpu > 0 ? pp1 / 1000 * r.Gpu / sumGpu : 0);
            }
            Rows = groups.Values.ToList();
            last = now; lastT = t;
        }
        public void Dispose() { pdh.Dispose(); pdhGpu.Dispose(); }
    }
}
