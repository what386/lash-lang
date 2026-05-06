namespace Lash.Cli.Services;

using System.Diagnostics;

internal sealed class ProcessRunnerService
{
    public int RunProcess(string fileName, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        return RunProcessWithLaunchState(fileName, args, workingDirectory).ExitCode;
    }

    public int RunProcessIgnoringStdout(string fileName, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory!;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.Error.WriteLine($"Failed to launch: {fileName}");
                return 1;
            }

            var stdoutDrainTask = Task.Run(() =>
            {
                while (true)
                {
                    var line = process.StandardOutput.ReadLine();
                    if (line is null)
                        break;
                }
            });

            process.WaitForExit();
            stdoutDrainTask.Wait();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to execute '{fileName}': {ex.Message}");
            return 1;
        }
    }

    private static (bool Launched, int ExitCode) RunProcessWithLaunchState(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory!;

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                Console.Error.WriteLine($"Failed to launch: {fileName}");
                return (false, 1);
            }

            process.WaitForExit();
            return (true, process.ExitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to execute '{fileName}': {ex.Message}");
            return (false, 1);
        }
    }
}
