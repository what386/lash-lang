namespace Lash.Cli.Commands;

using System.CommandLine;
using Lash.Cli.Services;

static class FormatCommand
{
    public static Command Create()
    {
        Argument<string[]> fileArgument = new("paths")
        {
            Description = "Path(s) to .lash file(s) or directories to format",
            Arity = ArgumentArity.OneOrMore
        };

        Option<bool> checkOption = new("--check")
        {
            Description = "Check formatting without modifying files",
            DefaultValueFactory = parseResult => false
        };

        var command = new Command("format", "Format Lash source files")
        {
            fileArgument,
            checkOption,
            SharedOptions.Verbose
        };

        command.SetAction(parseResult =>
        {
            var paths = parseResult.GetValue(fileArgument) ?? Array.Empty<string>();
            var check = parseResult.GetValue(checkOption);
            var verbose = parseResult.GetValue(SharedOptions.Verbose);
            var processRunner = new ProcessRunnerService();
            var compiler = new CompilerService(processRunner);
            return compiler.Format(paths, check, verbose);
        });

        return command;
    }
}
