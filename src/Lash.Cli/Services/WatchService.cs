namespace Lash.Cli.Services;

using System.Threading.Channels;

internal sealed class WatchService(CompilerService compilerService)
{
    public int Watch(IReadOnlyList<string> inputs, bool verbose = false)
    {
        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("At least one path is required.");
            return 1;
        }

        if (!compilerService.TryResolveCompilerPath(out var resolvedCompilerPath, verbose))
            return 1;

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var trackedFiles = new HashSet<string>(comparer);
        var explicitFileInputs = new HashSet<string>(comparer);
        var dynamicRoots = new List<string>();
        var watchedDirectories = new HashSet<string>(comparer);
        var roots = new List<WatchRoot>();

        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var fullPath = Path.GetFullPath(input);
            if (File.Exists(fullPath))
            {
                if (!fullPath.EndsWith(".lash", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"File must have a .lash extension: {input}");
                    return 1;
                }

                trackedFiles.Add(fullPath);
                explicitFileInputs.Add(fullPath);
                var parent = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
                roots.Add(new WatchRoot(parent, false));
                continue;
            }

            if (Directory.Exists(fullPath))
            {
                roots.Add(new WatchRoot(fullPath, true));
                dynamicRoots.Add(fullPath);
                foreach (var lashFile in Directory.EnumerateFiles(fullPath, "*.lash", SearchOption.AllDirectories))
                    trackedFiles.Add(Path.GetFullPath(lashFile));
                continue;
            }

            Console.Error.WriteLine($"Path does not exist: {input}");
            return 1;
        }

        if (trackedFiles.Count == 0)
        {
            Console.Error.WriteLine("No .lash files found to watch.");
            return 1;
        }

        Console.WriteLine($"Watching {trackedFiles.Count} .lash file(s). Press Ctrl+C to stop.");
        CompileFiles(resolvedCompilerPath, trackedFiles, verbose);

        var eventChannel = Channel.CreateUnbounded<FileEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var watchers = new List<FileSystemWatcher>();
        var stopRequested = false;
        var dirtyWhileBusy = false;
        var pendingFiles = new HashSet<string>(comparer);
        object sync = new();

        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopRequested = true;
            eventChannel.Writer.TryWrite(FileEvent.Stop());
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root.Path))
                    continue;
                if (!watchedDirectories.Add(root.Path))
                    continue;

                var watcher = new FileSystemWatcher(root.Path)
                {
                    IncludeSubdirectories = root.Recursive,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };

                watcher.Changed += (_, args) => EnqueuePath(eventChannel.Writer, args.FullPath);
                watcher.Created += (_, args) => EnqueuePath(eventChannel.Writer, args.FullPath);
                watcher.Deleted += (_, args) => EnqueuePath(eventChannel.Writer, args.FullPath, deleted: true);
                watcher.Renamed += (_, args) =>
                {
                    EnqueuePath(eventChannel.Writer, args.OldFullPath, deleted: true);
                    EnqueuePath(eventChannel.Writer, args.FullPath);
                };
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }

            while (!stopRequested)
            {
                var fileEvent = eventChannel.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
                if (fileEvent.Kind == FileEventKind.Stop)
                    break;

                lock (sync)
                {
                    ApplyFileEvent(fileEvent, trackedFiles, pendingFiles, explicitFileInputs, dynamicRoots);
                }

                Thread.Sleep(125);
                while (eventChannel.Reader.TryRead(out var extraEvent))
                {
                    if (extraEvent.Kind == FileEventKind.Stop)
                    {
                        stopRequested = true;
                        break;
                    }

                    lock (sync)
                    {
                        ApplyFileEvent(extraEvent, trackedFiles, pendingFiles, explicitFileInputs, dynamicRoots);
                    }
                }

                if (stopRequested)
                    break;

                HashSet<string> filesToCompile;
                lock (sync)
                {
                    if (pendingFiles.Count == 0)
                        continue;

                    filesToCompile = new HashSet<string>(pendingFiles, comparer);
                    pendingFiles.Clear();
                }

                var changedExistingFiles = filesToCompile.Where(File.Exists).ToArray();
                if (changedExistingFiles.Length == 0)
                    continue;

                CompileFiles(resolvedCompilerPath, changedExistingFiles, verbose);

                lock (sync)
                {
                    if (pendingFiles.Count > 0)
                        dirtyWhileBusy = true;
                }

                if (dirtyWhileBusy)
                {
                    HashSet<string> coalescedFiles;
                    lock (sync)
                    {
                        coalescedFiles = new HashSet<string>(pendingFiles, comparer);
                        pendingFiles.Clear();
                        dirtyWhileBusy = false;
                    }

                    var coalescedExistingFiles = coalescedFiles.Where(File.Exists).ToArray();
                    if (coalescedExistingFiles.Length > 0)
                        CompileFiles(resolvedCompilerPath, coalescedExistingFiles, verbose);
                }
            }
        }
        finally
        {
            if (cancelHandler is not null)
                Console.CancelKeyPress -= cancelHandler;

            foreach (var watcher in watchers)
                watcher.Dispose();
        }

        return 0;
    }

    private void CompileFiles(string resolvedCompilerPath, IEnumerable<string> files, bool verbose)
    {
        foreach (var file in files.OrderBy(static p => p, StringComparer.Ordinal))
        {
            var outputPath = compilerService.DeriveOutputPath(file);

            if (verbose)
                Console.Error.WriteLine($"[lash] compile {file} -> {outputPath}");

            var exitCode = compilerService.EmitBashWithCompilerPath(
                resolvedCompilerPath,
                file,
                outputPath,
                suppressCompilerStdout: false);

            if (exitCode != 0)
                Console.Error.WriteLine($"[lash] compile failed ({exitCode}): {file}");
            else
                Console.WriteLine($"Compiled: {file}");
        }
    }

    private static void ApplyFileEvent(
        FileEvent fileEvent,
        HashSet<string> trackedFiles,
        HashSet<string> pendingFiles,
        HashSet<string> explicitFileInputs,
        IReadOnlyList<string> dynamicRoots)
    {
        if (!fileEvent.Path.EndsWith(".lash", StringComparison.OrdinalIgnoreCase))
            return;

        var isKnownTracked = trackedFiles.Contains(fileEvent.Path);
        var isExplicitFile = explicitFileInputs.Contains(fileEvent.Path);
        var isDynamicRootFile = dynamicRoots.Any(root => IsPathUnderRoot(fileEvent.Path, root));

        if (fileEvent.Kind == FileEventKind.Deleted)
        {
            if (isKnownTracked && !isExplicitFile)
                trackedFiles.Remove(fileEvent.Path);
            pendingFiles.Remove(fileEvent.Path);
            return;
        }

        if (!File.Exists(fileEvent.Path))
            return;

        if (!isKnownTracked && !isExplicitFile && !isDynamicRootFile)
            return;

        trackedFiles.Add(fileEvent.Path);
        pendingFiles.Add(fileEvent.Path);
    }

    private static void EnqueuePath(ChannelWriter<FileEvent> writer, string path, bool deleted = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path);
        writer.TryWrite(deleted ? FileEvent.Deleted(fullPath) : FileEvent.Changed(fullPath));
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (path.Equals(root, comparison))
            return true;

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private readonly record struct WatchRoot(string Path, bool Recursive);

    private enum FileEventKind
    {
        Changed,
        Deleted,
        Stop
    }

    private readonly record struct FileEvent(FileEventKind Kind, string Path)
    {
        public static FileEvent Changed(string path) => new(FileEventKind.Changed, path);
        public static FileEvent Deleted(string path) => new(FileEventKind.Deleted, path);
        public static FileEvent Stop() => new(FileEventKind.Stop, string.Empty);
    }
}
