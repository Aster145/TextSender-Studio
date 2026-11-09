using System.Diagnostics;
using System.Text.Json;
using EasyCon.Script;
using EasyCon.Script.Bytecode;

// Optional performance probe, not a timing assertion in CI. All bytecode is
// compiled once, warmed, and reused. No UI, hardware, network or notifications.
var workloads = new Dictionary<string, EcxImage>();
foreach (var (name, source) in new Dictionary<string, string>
{
    ["compute"] = "$sum = 0\nFOR 2000000\n    $sum = ($sum + 7) % 1000000\nNEXT\nPRINT $sum",
    ["wait"] = "FOR 20\n    WAIT 20\nNEXT"
})
{
    var compiled = Compilation.CompileSource(source, new CompileOptions { UseDiskCache = false, UseProcessCache = false });
    if (compiled.Diagnostics.Any(d => d.IsError) || compiled.Image == null) throw new Exception("benchmark compilation failed");
    workloads.Add(name, compiled.Image);
}

var results = new List<object>();
foreach (var (workload, image) in workloads)
{
    var samples = Enumerable.Range(0, 3).Select(_ => new List<double>()).ToArray();
    // Rotate ordering to avoid always giving one mode the cold or hot run.
    for (int round = -3; round < 7; round++)
        for (int offset = 0; offset < 3; offset++)
        {
            int mode = (round + 3 + offset) % 3;
            var position = mode == 0 ? null : new EcxExecutionPosition();
            var host = new EcxHost { ExecutionPosition = position, WaitMs = ms => Thread.Sleep(ms) };
            long polls = 0;
            using var timer = mode == 2 ? new Timer(_ =>
            {
                long snapshot = position!.Snapshot;
                if (snapshot >= 0)
                {
                    int fn = (int)(snapshot >> 32), pc = (int)(snapshot & uint.MaxValue);
                    _ = image.Functions[fn].LineAt(pc);
                    Interlocked.Increment(ref polls);
                }
            }, null, 100, 100) : null;
            var watch = Stopwatch.StartNew();
            var status = EcxInterpreter.Run(image, host);
            watch.Stop();
            if (status != EcxInterpreter.OK) throw new Exception("benchmark VM failed: " + status);
            if (round >= 0) samples[mode].Add(watch.Elapsed.TotalMilliseconds);
        }
    double baseline = Median(samples[0]);
    for (int mode = 0; mode < 3; mode++)
    {
        double median = Median(samples[mode]);
        results.Add(new
        {
            workload, mode = new[] { "no_observer", "observer_display_off", "observer_poll_10hz" }[mode],
            median_ms = Math.Round(median, 3), min_ms = Math.Round(samples[mode].Min(), 3), max_ms = Math.Round(samples[mode].Max(), 3),
            relative_percent = Math.Round((median / baseline - 1) * 100, 2), runs = samples[mode].Count
        });
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), cpu_count = Environment.ProcessorCount, note = "VM-only; excludes compilation and WPF rendering; not a hardware timing guarantee", results }, new JsonSerializerOptions { WriteIndented = true }));

static double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
