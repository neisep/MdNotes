using BenchmarkDotNet.Running;

// Micro-benchmarks for the performance review's "verify before changing" findings
// (docs/performance-review.html#profiling). The vault-wide scan timings live in
// MdNotes.Tests/ScanBenchmark.cs instead: those measure disk latency, which a micro-benchmark cannot.
//
//   dotnet run -c Release --project MdNotes.Benchmarks -- --filter *
//
// Benchmark classes are added by the tasks that need them; the switcher finds them by reflection.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

public partial class Program;
