using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Fuse.Infrastructure.Persistence;

namespace Fuse.Web.Tests;

/// <summary>
/// EF Core materialises an entity through a constructor whose parameters match its properties when
/// one exists, so any logic in that constructor runs on every read. Legacy entities had such
/// constructors (e.g. appending "?w=1000" to image paths, which grew by one suffix per save).
/// Every entity must be read through a parameterless constructor.
/// </summary>
public sealed class EntityConstructionTests(TestApp app)
{
    [Fact]
    public void Entities_are_read_without_running_constructor_logic()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var offenders = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.ClrType.Namespace?.StartsWith("Fuse") == true)
            .Where(t => t.ConstructorBinding?.ParameterBindings.Count > 0)
            .Select(t => t.ClrType.Name)
            .Distinct()
            .Order()
            .ToList();

        offenders.ShouldBeEmpty($"these entities are read through a parameterised constructor: {string.Join(", ", offenders)}");
    }
}
