using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Local_Judge
{
    public sealed class PythonRunner
    {
        private const string DefaultPythonCodeFileName = "main.py";

        private Process? _runningProcess;
        private bool _stopRequested;

        public string PythonExecutablePath { get; set; } = EmbeddedPythonRuntime.ResolveDefaultExecutablePath();

        public bool IsRunning
        {
            get
            {
                Process? process = _runningProcess;
                return process is not null && !process.HasExited;
            }
        }

        public async Task<PythonExecutionResult> RunAsync(
            string code,
            string inputText,
            PythonExecutionLimits? limits = null,
            Action<string>? outputReceived = null,
            Action? processStarted = null)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A Python process is already running.");
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "LocalJudge", Guid.NewGuid().ToString("N"));
            string scriptPath = Path.Combine(tempDirectory, DefaultPythonCodeFileName);
            Process? process = null;
            var stopwatch = new Stopwatch();
            var statusLock = new object();
            PythonExecutionStatus executionStatus = PythonExecutionStatus.Completed;
            long peakWorkingSetBytes = 0;
            limits ??= PythonExecutionLimits.Default;

            bool TrySetStatus(PythonExecutionStatus status)
            {
                lock (statusLock)
                {
                    if (executionStatus != PythonExecutionStatus.Completed)
                    {
                        return false;
                    }

                    executionStatus = status;
                    return true;
                }
            }

            try
            {
                Directory.CreateDirectory(tempDirectory);
                await File.WriteAllTextAsync(scriptPath, code, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                process = CreateProcess(scriptPath, tempDirectory);
                _stopRequested = false;

                stopwatch.Start();
                if (!process.Start())
                {
                    throw new InvalidOperationException("Python process did not start.");
                }

                _runningProcess = process;
                processStarted?.Invoke();

                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();
                long capturedOutputBytes = 0;
                bool outputLimitExceeded = false;
                object outputLock = new();

                string CaptureOutput(string text)
                {
                    int? outputLimitBytes = limits.OutputLimitBytes;
                    if (outputLimitBytes is null || outputLimitBytes <= 0)
                    {
                        return text;
                    }

                    lock (outputLock)
                    {
                        if (outputLimitExceeded)
                        {
                            return string.Empty;
                        }

                        int textByteCount = Encoding.UTF8.GetByteCount(text);
                        if (capturedOutputBytes + textByteCount <= outputLimitBytes.Value)
                        {
                            capturedOutputBytes += textByteCount;
                            return text;
                        }

                        long remainingBytes = outputLimitBytes.Value - capturedOutputBytes;
                        string truncatedText = TruncateToUtf8ByteLimit(text, remainingBytes);
                        capturedOutputBytes += Encoding.UTF8.GetByteCount(truncatedText);
                        outputLimitExceeded = true;

                        if (TrySetStatus(PythonExecutionStatus.OutputLimitExceeded))
                        {
                            TryKillProcess(process);
                        }

                        return truncatedText;
                    }
                }

                using var limitMonitorCancellation = new CancellationTokenSource();
                Task timeLimitTask = StartTimeLimitMonitorAsync(
                    process,
                    limits.TimeLimit,
                    () =>
                    {
                        if (TrySetStatus(PythonExecutionStatus.TimeLimitExceeded))
                        {
                            TryKillProcess(process);
                        }
                    },
                    limitMonitorCancellation.Token);

                Task memoryLimitTask = StartMemoryLimitMonitorAsync(
                    process,
                    limits.MemoryLimitBytes,
                    workingSetBytes =>
                    {
                        if (workingSetBytes > peakWorkingSetBytes)
                        {
                            peakWorkingSetBytes = workingSetBytes;
                        }
                    },
                    () =>
                    {
                        if (TrySetStatus(PythonExecutionStatus.MemoryLimitExceeded))
                        {
                            TryKillProcess(process);
                        }
                    },
                    limitMonitorCancellation.Token);

                Task stdoutTask = ReadProcessStreamAsync(process.StandardOutput, stdoutBuilder, outputReceived, CaptureOutput);
                Task stderrTask = ReadProcessStreamAsync(process.StandardError, stderrBuilder, outputReceived, CaptureOutput);

                await TryWriteStandardInputAsync(process, inputText ?? string.Empty);

                await process.WaitForExitAsync();
                limitMonitorCancellation.Cancel();
                await Task.WhenAll(stdoutTask, stderrTask);
                await Task.WhenAll(timeLimitTask, memoryLimitTask);

                stopwatch.Stop();

                if (_stopRequested && executionStatus == PythonExecutionStatus.Completed)
                {
                    executionStatus = PythonExecutionStatus.Stopped;
                }

                return new PythonExecutionResult(
                    process.ExitCode,
                    executionStatus,
                    stopwatch.Elapsed,
                    stdoutBuilder.ToString(),
                    stderrBuilder.ToString(),
                    peakWorkingSetBytes,
                    limits);
            }
            finally
            {
                if (stopwatch.IsRunning)
                {
                    stopwatch.Stop();
                }

                if (process is not null)
                {
                    if (_runningProcess == process)
                    {
                        TryKillProcess(process);
                        _runningProcess = null;
                    }

                    process.Dispose();
                }

                TryDeleteDirectory(tempDirectory);
            }
        }

        public bool Stop()
        {
            Process? process = _runningProcess;
            if (process is null || process.HasExited)
            {
                return false;
            }

            _stopRequested = true;
            process.Kill(entireProcessTree: true);
            return true;
        }

        public async Task<string> GetVersionTextAsync()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PythonExecutablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("--version");

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return string.Empty;
            }

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            string versionText = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
            return versionText.Trim();
        }

        private Process CreateProcess(string scriptPath, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PythonExecutablePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add("-B");
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            startInfo.Environment["PYTHONUTF8"] = "1";

            return new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
        }

        private static async Task ReadProcessStreamAsync(
            StreamReader reader,
            StringBuilder captureBuilder,
            Action<string>? outputReceived,
            Func<string, string> captureOutput)
        {
            char[] buffer = new char[1024];

            while (true)
            {
                int readCount = await reader.ReadAsync(buffer, 0, buffer.Length);

                if (readCount <= 0)
                {
                    break;
                }

                string text = new string(buffer, 0, readCount);
                string capturedText = captureOutput(text);

                if (capturedText.Length > 0)
                {
                    captureBuilder.Append(capturedText);
                    outputReceived?.Invoke(capturedText);
                }
            }
        }

        private static async Task StartTimeLimitMonitorAsync(
            Process process,
            TimeSpan? timeLimit,
            Action limitExceeded,
            CancellationToken cancellationToken)
        {
            if (timeLimit is null || timeLimit <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                await Task.Delay(timeLimit.Value, cancellationToken);

                if (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    limitExceeded();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task StartMemoryLimitMonitorAsync(
            Process process,
            long? memoryLimitBytes,
            Action<long> workingSetObserved,
            Action limitExceeded,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (process.HasExited)
                    {
                        return;
                    }

                    process.Refresh();
                    long workingSetBytes = process.WorkingSet64;
                    workingSetObserved(workingSetBytes);

                    if (memoryLimitBytes is > 0 && workingSetBytes > memoryLimitBytes.Value)
                    {
                        limitExceeded();
                        return;
                    }

                    await Task.Delay(50, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch
            {
                // Memory monitoring is best-effort. Execution itself should decide the result.
            }
        }

        private static async Task TryWriteStandardInputAsync(Process process, string inputText)
        {
            try
            {
                await process.StandardInput.WriteAsync(inputText);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child process may close stdin before consuming all input.
            }
            catch (InvalidOperationException)
            {
                // The process may have exited or stdin may already be closed.
            }
        }

        private static string TruncateToUtf8ByteLimit(string text, long byteLimit)
        {
            if (byteLimit <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            long byteCount = 0;

            foreach (Rune rune in text.EnumerateRunes())
            {
                int runeByteCount = rune.Utf8SequenceLength;
                if (byteCount + runeByteCount > byteLimit)
                {
                    break;
                }

                builder.Append(rune.ToString());
                byteCount += runeByteCount;
            }

            return builder.ToString();
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    public sealed record PythonExecutionLimits(
        TimeSpan? TimeLimit,
        long? MemoryLimitBytes,
        int? OutputLimitBytes)
    {
        public const int DefaultOutputLimitBytes = 1024 * 1024;

        public static PythonExecutionLimits Default { get; } = new(
            TimeSpan.FromMilliseconds(2000),
            128L * 1024L * 1024L,
            DefaultOutputLimitBytes);
    }

    public enum PythonExecutionStatus
    {
        Completed,
        Stopped,
        TimeLimitExceeded,
        MemoryLimitExceeded,
        OutputLimitExceeded
    }

    public sealed record PythonExecutionResult(
        int ExitCode,
        PythonExecutionStatus Status,
        TimeSpan Elapsed,
        string StandardOutput,
        string StandardError,
        long PeakWorkingSetBytes,
        PythonExecutionLimits Limits)
    {
        public bool Stopped => Status == PythonExecutionStatus.Stopped;
        public bool TimedOut => Status == PythonExecutionStatus.TimeLimitExceeded;
        public bool MemoryLimitExceeded => Status == PythonExecutionStatus.MemoryLimitExceeded;
        public bool OutputLimitExceeded => Status == PythonExecutionStatus.OutputLimitExceeded;
        public bool LimitExceeded => TimedOut || MemoryLimitExceeded || OutputLimitExceeded;
        public bool Succeeded => Status == PythonExecutionStatus.Completed && ExitCode == 0;
    }
}
