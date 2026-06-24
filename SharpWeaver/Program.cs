using SharpWeaver;

/// <summary>Cecil IL post-processor command-line entry point.</summary>
public static class Program
{
    /// <summary>Program entry point.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code.</returns>
    public static int Main(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        string? assemblyPath = null;
        string? references = null;
        var dryRun = false;
        var verbose = false;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--assembly":
                        assemblyPath = RequireNextArg(args, ref i, "--assembly");
                        break;
                    case "--references":
                        references = RequireNextArg(args, ref i, "--references");
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown argument: {arg}");
                        PrintHelp();
                        return 1;
                }
            }
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintHelp();
            return 1;
        }

        return SharpWeaverRunner.Run(new SharpWeaverOptions
        {
            AssemblyPath = assemblyPath,
            References = references,
            DryRun = dryRun,
            Verbose = verbose,
        });
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var arg in args)
        {
            if (arg == flag)
            {
                return true;
            }
        }

        return false;
    }

    private static string RequireNextArg(string[] args, ref int index, string flagName)
    {
        if (index + 1 >= args.Length)
        {
            throw new CommandLineException($"Missing value for {flagName}.");
        }

        index++;
        return args[index];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            SharpWeaver — compile-time IL weaver for .NET

            Usage:
              SharpWeaver [options]

            Options:
              --assembly <path>       Path to the assembly to weave
              --references <paths>    Semicolon-separated reference assembly paths
              --dry-run               Resolve patches without writing IL
              --verbose               Print diagnostic output
              --help, -h              Show this help message
            """);
    }
}

/// <summary>Command-line argument parsing error.</summary>
public sealed class CommandLineException : Exception
{
    /// <summary>Creates a command-line exception.</summary>
    /// <param name="message">Error message.</param>
    public CommandLineException(string message)
        : base(message)
    {
    }
}
