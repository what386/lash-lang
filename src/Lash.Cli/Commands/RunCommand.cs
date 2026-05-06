namespace Lash.Cli.Commands;

using System.CommandLine;
using Lash.Cli.Services;

static class RunCommand
{
    public static Command Create()
    {
        Argument<string> fileArgument = new("file")
        {
            Description = "Path to the .lash file to run"
        };

        Option<bool> keepTempOption = new("--keep-temp")
        {
            Description = "Keep generated temporary Bash file",
            DefaultValueFactory = parseResult => false
        };

        Argument<string[]> argsArgument = new("args")
        {
            Description = "Arguments passed to the Lash program",
            Arity = ArgumentArity.ZeroOrMore
        };

        fileArgument.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>() ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError("File path cannot be empty.");
                return;
            }
            if (!value.EndsWith(".lash"))
            {
                result.AddError("File must have a .lash extension.");
            }
        });

        var command = new Command("run", "Compile a .lash file to a temporary script and run it with bash")
        {
            fileArgument,
            keepTempOption,
            argsArgument,
            SharedOptions.Verbose
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArgument)!;
            var keepTemp = parseResult.GetValue(keepTempOption);
            var args = parseResult.GetValue(argsArgument) ?? Array.Empty<string>();
            var verbose = parseResult.GetValue(SharedOptions.Verbose);
            var processRunner = new ProcessRunnerService();
            var compiler = new CompilerService(processRunner);
            var runService = new RunService(compiler, processRunner);
            return runService.Run(file, keepTemp, args, verbose);
        });

        return command;
    }
}
