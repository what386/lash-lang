namespace Lash.Cli.Commands;

using System.CommandLine;
using Lash.Cli.Services;

static class WatchCommand
{
    public static Command Create()
    {
        Argument<string[]> pathsArgument = new("paths")
        {
            Description = "Path(s) to .lash file(s) or directories to watch",
            Arity = ArgumentArity.OneOrMore
        };

        pathsArgument.Validators.Add(result =>
        {
            var paths = result.GetValueOrDefault<string[]>() ?? Array.Empty<string>();
            if (paths.Length == 0)
            {
                result.AddError("At least one path is required.");
                return;
            }

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.AddError("Path cannot be empty.");
                    continue;
                }

                if (Directory.Exists(path))
                    continue;

                if (!File.Exists(path))
                {
                    result.AddError($"Path does not exist: {path}");
                    continue;
                }

                if (!path.EndsWith(".lash", StringComparison.OrdinalIgnoreCase))
                    result.AddError($"File must have a .lash extension: {path}");
            }
        });

        var command = new Command("watch", "Watch .lash file(s) and recompile changed files")
        {
            pathsArgument,
            SharedOptions.Verbose
        };

        command.SetAction(parseResult =>
        {
            var paths = parseResult.GetValue(pathsArgument) ?? Array.Empty<string>();
            var verbose = parseResult.GetValue(SharedOptions.Verbose);
            var processRunner = new ProcessRunnerService();
            var compiler = new CompilerService(processRunner);
            var watchService = new WatchService(compiler);
            return watchService.Watch(paths, verbose);
        });

        return command;
    }
}
