using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Api_source_must_not_reference_AI_or_private_module_storage()
    {
        var root = FindRepositoryRoot();
        var api = Path.Combine(root, "backend", "src", "Kanoonyar.Api");
        var sources = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .ToArray();
        Assert.NotEmpty(sources);
        foreach (var path in sources)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("kanoonyar_ai", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Modules.PrivatePersistence", source, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "backend")) &&
                Directory.Exists(Path.Combine(directory.FullName, "frontend")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found");
    }
}
