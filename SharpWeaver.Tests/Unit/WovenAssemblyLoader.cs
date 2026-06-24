using System.Reflection;
using System.Runtime.Loader;

namespace SharpWeaver.Tests;

/// <summary>Loads woven assemblies and their dependencies in unit tests.</summary>
internal static class WovenAssemblyLoader
{
    /// <summary>Loads a woven assembly from disk, resolving dependencies from the same directory and additional reference paths.</summary>
    /// <param name="assemblyPath">Primary assembly path.</param>
    /// <param name="referencePaths">Additional reference assembly paths.</param>
    /// <returns>The loaded primary assembly.</returns>
    public static Assembly Load(string assemblyPath, IReadOnlyList<string> referencePaths)
    {
        var resolver = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddAssemblyPaths(resolver, Path.GetDirectoryName(assemblyPath)!);
        foreach (var referencePath in referencePaths)
        {
            if (File.Exists(referencePath))
            {
                resolver[Path.GetFileNameWithoutExtension(referencePath)] = referencePath;
            }
        }

        var context = new ResolverAssemblyLoadContext(assemblyPath, resolver);
        return context.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
    }

    private static void AddAssemblyPaths(Dictionary<string, string> resolver, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(directory, "*.dll"))
        {
            resolver[Path.GetFileNameWithoutExtension(file)] = file;
        }
    }

    private sealed class ResolverAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly string _mainAssemblyPath;
        private readonly Dictionary<string, string> _assemblyPaths;

        public ResolverAssemblyLoadContext(string mainAssemblyPath, Dictionary<string, string> assemblyPaths)
            : base(isCollectible: true)
        {
            _mainAssemblyPath = mainAssemblyPath;
            _assemblyPaths = assemblyPaths;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null
                && _assemblyPaths.TryGetValue(assemblyName.Name, out var path))
            {
                return LoadFromAssemblyPath(path);
            }

            return null;
        }
    }
}
