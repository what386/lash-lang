namespace Lash.Cli.Services;

internal sealed class CompilerService(ProcessRunnerService processRunner)
{
    private string? compilerPath;
    private string? formatterPath;

    public int Check(string inputPath, bool verbose = false)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"File not found: {inputPath}");
            return 1;
        }

        if (!TryResolveCompilerPath(out var resolvedCompilerPath, verbose))
            return 1;

        return processRunner.RunProcess(resolvedCompilerPath, [inputPath, "--check"]);
    }

    public int Compile(string inputPath, string outputPath, bool verbose = false)
    {
        return EmitBash(inputPath, outputPath, verbose, suppressCompilerStdout: false);
    }

    public int EmitBash(string inputPath, string outputPath, bool verbose, bool suppressCompilerStdout)
    {
        if (!TryResolveCompilerPath(out var resolvedCompilerPath, verbose))
            return 1;

        return EmitBashWithCompilerPath(resolvedCompilerPath, inputPath, outputPath, suppressCompilerStdout);
    }

    public int EmitBashWithCompilerPath(string resolvedCompilerPath, string inputPath, string outputPath, bool suppressCompilerStdout)
    {
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"File not found: {inputPath}");
            return 1;
        }

        var compilerArgs = new[] { inputPath, "--emit-bash", outputPath };
        var exitCode = suppressCompilerStdout
            ? processRunner.RunProcessIgnoringStdout(resolvedCompilerPath, compilerArgs)
            : processRunner.RunProcess(resolvedCompilerPath, compilerArgs);
        if (exitCode != 0)
            return exitCode;

        TryMarkExecutable(outputPath);
        return 0;
    }

    public int Format(IReadOnlyList<string> paths, bool check, bool verbose = false)
    {
        if (!TryResolveFormatterPath(out var resolvedFormatterPath, verbose))
            return 1;

        var arguments = new List<string>();
        if (check)
            arguments.Add("--check");
        arguments.AddRange(paths);

        return processRunner.RunProcess(resolvedFormatterPath, arguments);
    }

    public bool TryResolveCompilerPath(out string resolvedCompilerPath, bool verbose = false)
    {
        if (!string.IsNullOrWhiteSpace(compilerPath))
        {
            resolvedCompilerPath = compilerPath;
            if (verbose)
                Console.Error.WriteLine($"[lash] using compiler: {resolvedCompilerPath}");
            return true;
        }

        try
        {
            compilerPath = ToolResolver.ResolveCompilerPath();
            resolvedCompilerPath = compilerPath;
            if (verbose)
                Console.Error.WriteLine($"[lash] using compiler: {resolvedCompilerPath}");
            return true;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            resolvedCompilerPath = string.Empty;
            return false;
        }
    }

    public string DeriveOutputPath(string inputPath)
    {
        return Path.Combine(
            Path.GetDirectoryName(inputPath) ?? ".",
            $"{Path.GetFileNameWithoutExtension(inputPath)}.sh");
    }

    private bool TryResolveFormatterPath(out string resolvedFormatterPath, bool verbose = false)
    {
        if (!string.IsNullOrWhiteSpace(formatterPath))
        {
            resolvedFormatterPath = formatterPath;
            if (verbose)
                Console.Error.WriteLine($"[lash] using formatter: {resolvedFormatterPath}");
            return true;
        }

        try
        {
            formatterPath = ToolResolver.ResolveFormatterPath();
            resolvedFormatterPath = formatterPath;
            if (verbose)
                Console.Error.WriteLine($"[lash] using formatter: {resolvedFormatterPath}");
            return true;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            resolvedFormatterPath = string.Empty;
            return false;
        }
    }

    private static void TryMarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Console.Error.WriteLine($"Warning: unable to set executable permissions on '{path}': {ex.Message}");
        }
    }
}
