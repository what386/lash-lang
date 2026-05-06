namespace Lash.Cli.Commands;

using System.CommandLine;
using Lash.Cli.Services;


static class CompileCommand
{
    public static Command Create()
    {
        Argument<string> fileArgument = new("file")
        {
            Description = "Path to the .lash file to compile"
        };

        Option<string?> outputOption = new("-o", "--output")
        {
            Description = "Output file path (defaults to <input>.sh)"
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

        var command = new Command("compile", "Compile a .lash file to Bash")
        {
            fileArgument,
            outputOption,
            SharedOptions.Verbose
        };

        command.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileArgument)!;
            var output = parseResult.GetValue(outputOption);
            var verbose = parseResult.GetValue(SharedOptions.Verbose);
            output ??= Path.Combine(
                Path.GetDirectoryName(file) ?? ".",
                $"{Path.GetFileNameWithoutExtension(file)}.sh");

            var processRunner = new ProcessRunnerService();
            var compiler = new CompilerService(processRunner);
            return compiler.Compile(file, output, verbose);
        });

        return command;
    }
}
