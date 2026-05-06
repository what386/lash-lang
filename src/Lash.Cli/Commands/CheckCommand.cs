namespace Lash.Cli.Commands;

using System.CommandLine;
using Lash.Cli.Services;

static class CheckCommand
{
    public static Command Create()
    {
        Argument<string> fileArgument = new("file")
        {
            Description = "Path to the .lash file to check"
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

        var command = new Command("check", "Validate a .lash file without emitting output")
        {
            fileArgument,
            SharedOptions.Verbose
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArgument);
            var verbose = parseResult.GetValue(SharedOptions.Verbose);
            var processRunner = new ProcessRunnerService();
            var compiler = new CompilerService(processRunner);
            return compiler.Check(file!, verbose);
        });

        return command;
    }
}
