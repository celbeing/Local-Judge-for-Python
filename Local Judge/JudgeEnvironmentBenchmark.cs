using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Local_Judge
{
    public sealed class JudgeEnvironmentBenchmark
    {
        private const double MinTimeMultiplier = 1.0;
        private const double MaxTimeMultiplier = 2.5;
        private const int MinExtraTimeMs = 300;
        private const int MaxExtraTimeMs = 2000;
        private const int MinExtraMemoryMb = 64;
        private const int MaxExtraMemoryMb = 256;

        private readonly PythonRunner _pythonRunner;

        public JudgeEnvironmentBenchmark(PythonRunner pythonRunner)
        {
            _pythonRunner = pythonRunner;
        }

        public async Task<JudgeEnvironmentBenchmarkResult> RunAsync()
        {
            try
            {
                PythonExecutionResult startupResult = await RunCodeAsync("pass");
                double startupElapsedMs = startupResult.Elapsed.TotalMilliseconds;

                var sampleResults = new List<JudgeBenchmarkSampleResult>();

                foreach (BenchmarkSampleDefinition sample in CreateSamples())
                {
                    PythonExecutionResult result = await RunCodeAsync(sample.Code);
                    double actualElapsedMs = Math.Max(result.Elapsed.TotalMilliseconds, 1);
                    double slowdown = sample.IncludeInTimeMultiplier
                        ? actualElapsedMs / sample.ReferenceElapsedMs
                        : 1.0;

                    sampleResults.Add(new JudgeBenchmarkSampleResult(
                        sample.Name,
                        sample.Complexity,
                        sample.ReferenceElapsedMs,
                        actualElapsedMs,
                        slowdown,
                        result.PeakWorkingSetBytes,
                        sample.IsMemorySample));
                }

                double[] timeSlowdowns = sampleResults
                    .Where(sample => !sample.IsMemorySample)
                    .Select(sample => sample.Slowdown)
                    .OrderBy(value => value)
                    .ToArray();

                double timeMultiplier = Clamp(Median(timeSlowdowns), MinTimeMultiplier, MaxTimeMultiplier);
                int extraTimeMs = (int)Math.Round(Clamp(startupElapsedMs * 1.5, MinExtraTimeMs, MaxExtraTimeMs));

                long memoryPeakBytes = sampleResults
                    .Where(sample => sample.IsMemorySample)
                    .Select(sample => sample.PeakWorkingSetBytes)
                    .DefaultIfEmpty(0)
                    .Max();
                double memoryPeakMb = memoryPeakBytes / 1024d / 1024d;
                int extraMemoryMb = (int)Math.Ceiling(Clamp(memoryPeakMb + 32, MinExtraMemoryMb, MaxExtraMemoryMb));

                return new JudgeEnvironmentBenchmarkResult(
                    Succeeded: true,
                    IsFallback: false,
                    ErrorMessage: string.Empty,
                    EmptyPythonStartupMs: startupElapsedMs,
                    TimeMultiplier: timeMultiplier,
                    ExtraTimeMs: extraTimeMs,
                    ExtraMemoryMb: extraMemoryMb,
                    Samples: sampleResults);
            }
            catch (Win32Exception ex)
            {
                return JudgeEnvironmentBenchmarkResult.CreateFallback(ex.Message);
            }
            catch (Exception ex)
            {
                return JudgeEnvironmentBenchmarkResult.CreateFallback(ex.Message);
            }
        }

        private async Task<PythonExecutionResult> RunCodeAsync(string code)
        {
            var limits = new PythonExecutionLimits(
                TimeSpan.FromSeconds(8),
                null,
                16 * 1024);

            PythonExecutionResult result = await _pythonRunner.RunAsync(code, string.Empty, limits);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"벤치마크 Python 코드가 정상 종료되지 않았습니다. Status={result.Status}, ExitCode={result.ExitCode}");
            }

            return result;
        }

        private static IReadOnlyList<BenchmarkSampleDefinition> CreateSamples()
        {
            return new[]
            {
                new BenchmarkSampleDefinition(
                    "Linear loop",
                    "O(N)",
                    """
                    acc = 0
                    for i in range(2_800_000):
                        acc = (acc + ((i * 31) ^ (i >> 3))) & 0xFFFFFFFF
                    print(acc)
                    """,
                    ReferenceElapsedMs: 450,
                    IncludeInTimeMultiplier: true,
                    IsMemorySample: false),

                new BenchmarkSampleDefinition(
                    "Sort",
                    "O(N log N)",
                    """
                    data = []
                    x = 2463534242
                    for _ in range(120_000):
                        x ^= (x << 13) & 0xFFFFFFFF
                        x ^= x >> 17
                        x ^= (x << 5) & 0xFFFFFFFF
                        data.append(x)
                    data.sort()
                    print(data[len(data) // 2])
                    """,
                    ReferenceElapsedMs: 260,
                    IncludeInTimeMultiplier: true,
                    IsMemorySample: false),

                new BenchmarkSampleDefinition(
                    "Nested loop",
                    "O(N^2)",
                    """
                    acc = 0
                    n = 1250
                    for i in range(n):
                        row = i * 17
                        for j in range(n):
                            acc += (row + j * 31) & 7
                    print(acc)
                    """,
                    ReferenceElapsedMs: 300,
                    IncludeInTimeMultiplier: true,
                    IsMemorySample: false),

                new BenchmarkSampleDefinition(
                    "Permutation search",
                    "O(N!)",
                    """
                    import itertools

                    acc = 0
                    for p in itertools.permutations(range(9)):
                        acc += p[0] * 17 + p[3]
                    print(acc)
                    """,
                    ReferenceElapsedMs: 220,
                    IncludeInTimeMultiplier: true,
                    IsMemorySample: false),

                new BenchmarkSampleDefinition(
                    "Memory allocation",
                    "Memory",
                    """
                    import time

                    size = 48 * 1024 * 1024
                    buf = bytearray(size)
                    for i in range(0, size, 4096):
                        buf[i] = i & 255
                    time.sleep(0.2)
                    print(len(buf))
                    """,
                    ReferenceElapsedMs: 250,
                    IncludeInTimeMultiplier: false,
                    IsMemorySample: true)
            };
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 1.0;
            }

            int middle = values.Count / 2;
            if (values.Count % 2 == 1)
            {
                return values[middle];
            }

            return (values[middle - 1] + values[middle]) / 2.0;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        private sealed record BenchmarkSampleDefinition(
            string Name,
            string Complexity,
            string Code,
            double ReferenceElapsedMs,
            bool IncludeInTimeMultiplier,
            bool IsMemorySample);
    }

    public sealed record JudgeBenchmarkSampleResult(
        string Name,
        string Complexity,
        double ReferenceElapsedMs,
        double ActualElapsedMs,
        double Slowdown,
        long PeakWorkingSetBytes,
        bool IsMemorySample);

    public sealed record JudgeEnvironmentBenchmarkResult(
        bool Succeeded,
        bool IsFallback,
        string ErrorMessage,
        double EmptyPythonStartupMs,
        double TimeMultiplier,
        int ExtraTimeMs,
        int ExtraMemoryMb,
        IReadOnlyList<JudgeBenchmarkSampleResult> Samples)
    {
        public static JudgeEnvironmentBenchmarkResult DefaultFallback { get; } = CreateFallback("기본 보정값");

        public static JudgeEnvironmentBenchmarkResult CreateFallback(string errorMessage)
        {
            return new JudgeEnvironmentBenchmarkResult(
                Succeeded: false,
                IsFallback: true,
                ErrorMessage: errorMessage,
                EmptyPythonStartupMs: 0,
                TimeMultiplier: 1.0,
                ExtraTimeMs: 500,
                ExtraMemoryMb: 64,
                Samples: Array.Empty<JudgeBenchmarkSampleResult>());
        }

        public int ApplyTimeLimitMs(int idealTimeLimitMs)
        {
            return (int)Math.Ceiling(idealTimeLimitMs * TimeMultiplier + ExtraTimeMs);
        }

        public int ApplyMemoryLimitMb(int idealMemoryLimitMb)
        {
            return idealMemoryLimitMb + ExtraMemoryMb;
        }
    }
}
