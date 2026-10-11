using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace Kanoonyar.Api.Tests;

public sealed class ProjectReferenceBoundaryTests
{
    [Theory]
    [InlineData("../Kanoonyar.Contracts/Kanoonyar.Contracts.csproj", false)]
    [InlineData("../Kanoonyar.AiRuntime/Kanoonyar.AiRuntime.csproj", true)]
    [InlineData("../Kanoonyar.PrivatePersistence/Kanoonyar.PrivatePersistence.csproj", true)]
    public void Project_reference_guard_has_explicit_positive_and_negative_fixtures(string reference, bool forbidden)
    {
        Assert.Equal(forbidden, IsForbidden(reference));
    }

    [Fact]
    public void Api_project_does_not_reference_AI_runtime_or_private_persistence()
    {
        var root = FindRepositoryRoot();
        var project = Path.Combine(root, "backend", "src", "Kanoonyar.Api", "Kanoonyar.Api.csproj");
        var document = XDocument.Load(project);
        var references = document.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include")).Where(value => value is not null);
        foreach (var reference in references)
            Assert.False(IsForbidden(reference!), $"Forbidden API project reference: {reference}");
    }

    private static bool IsForbidden(string reference) =>
        reference.Contains("AiRuntime", StringComparison.OrdinalIgnoreCase) ||
        reference.Contains("PrivatePersistence", StringComparison.OrdinalIgnoreCase);

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
