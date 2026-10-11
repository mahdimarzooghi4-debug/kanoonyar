using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData("using kanoonyar_ai;", true)]
    [InlineData("using Modules.PrivatePersistence;", true)]
    [InlineData("using Kanoonyar.Contracts;", false)]
    public void Forbidden_reference_detector_has_negative_and_positive_cases(string source, bool forbidden)
    {
        Assert.Equal(forbidden, ContainsForbiddenReference(source));
    }

    [Fact]
    public void Api_source_must_not_reference_AI_or_private_module_storage()
    {
        var root = FindRepositoryRoot();
        var api = Path.Combine(root, "backend", "src", "Kanoonyar.Api");
        var sources = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(segment => segment is "obj" or "bin"))
            .ToArray();
        Assert.NotEmpty(sources);
        foreach (var path in sources)
            Assert.False(ContainsForbiddenReference(File.ReadAllText(path)), $"Forbidden reference: {path}");
    }

    private static bool ContainsForbiddenReference(string source) =>
        source.Contains("kanoonyar_ai", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("Modules.PrivatePersistence", StringComparison.Ordinal);

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
