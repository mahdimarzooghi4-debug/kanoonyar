using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class ProjectGraphBoundaryTests
{
    [Theory]
    [InlineData("Kanoonyar.Contracts", "Kanoonyar.Api", false)]
    [InlineData("Kanoonyar.Api", "Kanoonyar.Contracts", true)]
    [InlineData("Kanoonyar.Contracts", "Kanoonyar.PrivatePersistence", false)]
    [InlineData("Kanoonyar.Api", "Kanoonyar.Contracts", true)]
    public void Boundary_detector_rejects_disallowed_project_edges(string source, string target, bool allowed)
    {
        Assert.Equal(allowed, IsAllowed(source, target));
    }

    [Fact]
    public void Production_project_graph_has_only_approved_edges()
    {
        var root = FindRepositoryRoot();
        var src = Path.Combine(root, "backend", "src");
        foreach (var project in Directory.GetFiles(src, "*.csproj", SearchOption.AllDirectories))
        {
            var source = Path.GetFileNameWithoutExtension(project);
            var document = XDocument.Load(project);
            foreach (var edge in document.Descendants().Where(x => x.Name.LocalName == "ProjectReference"))
            {
                var include = (string?)edge.Attribute("Include");
                Assert.False(string.IsNullOrWhiteSpace(include), "ProjectReference missing Include");
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include!.Replace('\\', Path.DirectorySeparatorChar)));
                Assert.True(File.Exists(resolved), "Unresolved project reference: " + resolved);
                var target = Path.GetFileNameWithoutExtension(resolved);
                Assert.True(IsAllowed(source, target), "Disallowed project edge: " + source + " -> " + target);
            }
        }
    }

    private static bool IsAllowed(string source, string target) =>
        source == "Kanoonyar.Api" && target == "Kanoonyar.Contracts";

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "backend")) && Directory.Exists(Path.Combine(directory.FullName, "frontend")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found");
    }
}
