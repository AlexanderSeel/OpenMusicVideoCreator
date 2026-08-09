using System.Reflection;
using OpenMusicVideoCreator.Application.SystemInfo;
using DomainMarker = OpenMusicVideoCreator.Domain.AssemblyMarker;
using InfrastructureMarker = OpenMusicVideoCreator.Infrastructure.AssemblyMarker;

namespace OpenMusicVideoCreator.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_DoesNotReferenceOuterLayers()
    {
        AssertDoesNotReference(
            typeof(DomainMarker).Assembly,
            "OpenMusicVideoCreator.Application",
            "OpenMusicVideoCreator.Infrastructure",
            "OpenMusicVideoCreator.Api");
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureOrApi()
    {
        AssertDoesNotReference(
            typeof(SystemVersionResponse).Assembly,
            "OpenMusicVideoCreator.Infrastructure",
            "OpenMusicVideoCreator.Api");
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        AssertDoesNotReference(
            typeof(InfrastructureMarker).Assembly,
            "OpenMusicVideoCreator.Api");
    }

    private static void AssertDoesNotReference(Assembly assembly, params string[] forbiddenAssemblies)
    {
        var references = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbidden in forbiddenAssemblies)
        {
            Assert.DoesNotContain(forbidden, references);
        }
    }
}
