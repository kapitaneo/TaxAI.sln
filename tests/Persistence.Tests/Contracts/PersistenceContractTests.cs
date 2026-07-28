using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistence;

namespace Persistence.Tests.Contracts;

/// <summary>
/// The Wave 0 contract for the storage layer: one way in, no queryable out, and
/// no behaviour yet.
/// </summary>
public sealed class PersistenceContractTests
{
    private static readonly Assembly PersistenceAssembly = typeof(PersistenceRegistration).Assembly;

    [Fact]
    public void TheModuleExportsOnlyItsRegistrationExtension()
    {
        // The context, the configurations, and the port implementations are all
        // internal. The only supported way into this module is the registration
        // extension the composition root calls.
        Assert.Equal(
            new[] { "Persistence.PersistenceRegistration" },
            PersistenceAssembly
                .GetExportedTypes()
                .Select(type => type.FullName!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheRegistrationExtensionTakesAConnectionStringAndReturnsTheCollection()
    {
        var method = typeof(PersistenceRegistration)
            .GetMethod(nameof(PersistenceRegistration.AddTaxAiPersistence))!;

        Assert.Equal(
            new[] { typeof(IServiceCollection), typeof(string) },
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        Assert.Equal(typeof(IServiceCollection), method.ReturnType);
    }

    [Fact]
    public void TheContextIsInternalAndSealed()
    {
        var context = PersistenceAssembly.GetType("Persistence.TaxAiDbContext")!;

        Assert.NotNull(context);
        Assert.False(context.IsPublic);
        Assert.True(context.IsSealed);
        Assert.True(typeof(DbContext).IsAssignableFrom(context));
    }

    [Fact]
    public void TheContextDeclaresNoSetTypedMember()
    {
        // A context that hands out a queryable hands out the ability to write an
        // unscoped query, and every tenant guarantee then rests on each caller
        // remembering a firm predicate. Declared members only: DbContext's own
        // inherited surface is not this module's choice.
        var context = PersistenceAssembly.GetType("Persistence.TaxAiDbContext")!;

        var offenders = context
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.DeclaredOnly)
            .Where(member => member is PropertyInfo property &&
                             property.PropertyType.Name.StartsWith("DbSet", StringComparison.Ordinal))
            .Select(member => member.Name)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NoGenericRepositoryAbstractionExists()
    {
        var generic = PersistenceAssembly
            .GetTypes()
            .Where(type =>
                type.Name.Contains("Repository", StringComparison.Ordinal) &&
                type.IsGenericTypeDefinition)
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(generic);
    }

    [Fact]
    public void EveryDeclaredBehaviourStillThrowsNotImplemented()
    {
        // The proof that Wave 0 is a contract and not a half-built feature.
        Assert.Throws<NotImplementedException>(
            () => new ServiceCollection().AddTaxAiPersistence("unused"));
    }
}
