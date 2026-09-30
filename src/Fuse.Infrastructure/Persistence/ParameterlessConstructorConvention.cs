using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Fuse.Infrastructure.Persistence;

/// <summary>
/// EF Core prefers a constructor whose parameters match mapped properties, so any logic in it runs on
/// every read. Legacy entities' constructors normalise values (one appended "?w=1000" to image paths,
/// so paths grew by a suffix on every load and save). Our entities are always read through their
/// parameterless constructor and then have their properties set. Enforced by EntityConstructionTests.
/// </summary>
internal sealed class ParameterlessConstructorConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.ClrType.Namespace?.StartsWith("Fuse", StringComparison.Ordinal) != true)
                continue;
            var constructor = entityType.ClrType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
            if (constructor is not null)
            {
                // EF has no public setter for this at convention time. The internal one is guarded by
                // EntityConstructionTests: if an EF upgrade changes it, that test fails (not our data).
#pragma warning disable EF1001
                ((Microsoft.EntityFrameworkCore.Metadata.Internal.EntityType)entityType)
                    .SetConstructorBinding(new ConstructorBinding(constructor, []), ConfigurationSource.Convention);
#pragma warning restore EF1001
            }
        }
    }
}
