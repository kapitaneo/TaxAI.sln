using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Persistence;

namespace Persistence.Tests.Contracts;

/// <summary>
/// The durable contract for the storage layer: one way in and no queryable out.
/// These assertions remain valid as later waves replace scaffold bodies.
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
        // Spec 017 T01 adds exactly one more export: the two constants naming
        // the synthetic development identity the seeder writes.
        Assert.Equal(
            new[]
            {
                "Persistence.DevelopmentFixtureIdentity",
                "Persistence.PersistenceRegistration",
            },
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
            .GetMethod(
                nameof(PersistenceRegistration.AddTaxAiPersistence),
                new[] { typeof(IServiceCollection), typeof(string) })!;

        Assert.Equal(
            new[] { typeof(IServiceCollection), typeof(string) },
            method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());

        Assert.Equal(typeof(IServiceCollection), method.ReturnType);

        // Spec 017 T01: the one approved overload states the Development-local
        // database allowance explicitly and is otherwise the same entry point.
        var allowance = typeof(PersistenceRegistration)
            .GetMethod(
                nameof(PersistenceRegistration.AddTaxAiPersistence),
                new[] { typeof(IServiceCollection), typeof(string), typeof(bool) })!;

        Assert.Equal(typeof(IServiceCollection), allowance.ReturnType);
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

    [Fact] // Spec 016 T06: the reconciler adds no public surface and no queryable.
    public void TheRecoveryReconcilerIsAnInternalSealedPortImplementation()
    {
        var reconciler = PersistenceAssembly.GetType("Persistence.Services.SqlRecoveryReconciler")!;

        Assert.NotNull(reconciler);
        Assert.False(reconciler.IsPublic);
        Assert.True(reconciler.IsSealed);
        Assert.Contains(typeof(Tenancy.Contracts.IRecoveryReconciler), reconciler.GetInterfaces());
        Assert.DoesNotContain(
            reconciler.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            method => method.ReturnType.Name.Contains("Queryable", StringComparison.Ordinal) ||
                      method.ReturnType.Name.StartsWith("DbSet", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRegistrationEntryPointRemainsTheSingleExtensionMethod()
    {
        // Still one entry point by name. Spec 017 T01 declares it as exactly
        // two overloads: the original, and the one that states the
        // Development-local database allowance. Nothing else is public.
        var methods = typeof(PersistenceRegistration)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.Equal(2, methods.Length);
        Assert.All(
            methods,
            method =>
            {
                Assert.Equal(nameof(PersistenceRegistration.AddTaxAiPersistence), method.Name);
                Assert.True(method.IsDefined(typeof(ExtensionAttribute), inherit: false));
                Assert.Equal(typeof(IServiceCollection), method.ReturnType);
            });
        Assert.Equal(
            new[] { typeof(IServiceCollection), typeof(string) },
            methods[0].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        Assert.Equal(
            new[] { typeof(IServiceCollection), typeof(string), typeof(bool) },
            methods[1].GetParameters().Select(parameter => parameter.ParameterType).ToArray());
    }
}
