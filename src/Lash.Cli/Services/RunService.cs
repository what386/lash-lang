namespace Lash.Cli.Services;

internal sealed class RunService(CompilerService compilerService, ProcessRunnerService processRunner)
{
    public int Run(string inputPath, bool keepTemp, IReadOnlyList<string> args, bool verbose = false)
    {
        var fullInputPath = Path.GetFullPath(inputPath);
        var tempPath = Path.Combine(Path.GetTempPath(), $"lash-run-{Guid.NewGuid():N}.sh");
        var compileExitCode = compilerService.EmitBash(fullInputPath, tempPath, verbose, suppressCompilerStdout: true);
        if (compileExitCode != 0)
            return compileExitCode;

        try
        {
            var exitCode = processRunner.RunProcess("bash", [tempPath, .. args], Environment.CurrentDirectory);
            return exitCode;
        }
        finally
        {
            if (keepTemp)
            {
                Console.WriteLine($"Kept generated script: {tempPath}");
            }
            else if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
