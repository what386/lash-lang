namespace Lash.Cli;

using System.CommandLine;

using Lash.Cli.Commands;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Lash CLI");

        rootCommand.SetAction(parseResult =>
        {
            Console.WriteLine("Use `lash --help` to see available commands.");
            return 0;
        });

        RegisterCommands(rootCommand);
        return await rootCommand.Parse(args).InvokeAsync();
    }

    public static void RegisterCommands(RootCommand root)
    {
        root.Subcommands.Add(CompileCommand.Create());
        root.Subcommands.Add(CheckCommand.Create());
        root.Subcommands.Add(FormatCommand.Create());
        root.Subcommands.Add(RunCommand.Create());
        root.Subcommands.Add(WatchCommand.Create());
    }
}
