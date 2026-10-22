using System.Reflection;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Application.Features.Connections.Queries.GetConnections;
using MessagingToolbox.Domain.Entities;
using MessagingToolbox.Infrastructure.Persistence;
using MessagingToolbox.Providers.AzureServiceBus;
using MessagingToolbox.Providers.RabbitMQ;
using MessagingToolbox.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules of architecture doc §3 plus the DevOps amendment:
/// Abstractions is dependency-free, Domain/Application stay SDK-free, Providers reference
/// Abstractions (never the other way, and never each other), Infrastructure stays provider-free,
/// Web components never bypass the Application layer.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IMessagingProvider).Assembly;
    private static readonly Assembly DomainAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(GetConnectionsQuery).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ToolboxDbContext).Assembly;
    private static readonly Assembly WebAssembly = typeof(Home).Assembly;

    // Every broker provider; a new provider adds its assembly and SDK namespaces here.
    private static readonly Assembly[] ProviderAssemblies =
    [
        typeof(AzureServiceBusProvider).Assembly,
        typeof(RabbitMqProvider).Assembly,
    ];

    private static readonly string[] BrokerSdkNamespaces = ["Azure.Messaging.ServiceBus", "Azure.Core", "RabbitMQ.Client"];

    private const string ProvidersNamespace = "MessagingToolbox.Providers";

    public static TheoryData<string> ProviderAssemblyNames()
    {
        TheoryData<string> data = [];
        foreach (Assembly assembly in ProviderAssemblies)
        {
            data.Add(assembly.GetName().Name!);
        }

        return data;
    }

    [Fact]
    public void Abstractions_DoesNotDependOnOtherLayers()
    {
        TestResult result = Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "MessagingToolbox.Domain",
                "MessagingToolbox.Application",
                "MessagingToolbox.Infrastructure",
                ProvidersNamespace,
                "MessagingToolbox.Web")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Domain_DoesNotReferenceFrameworkOrSdkDependencies()
    {
        TestResult result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Mediator", .. BrokerSdkNamespaces])
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureProvidersOrSdks()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                ["MessagingToolbox.Infrastructure", ProvidersNamespace, "Microsoft.AspNetCore", .. BrokerSdkNamespaces])
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Theory]
    [MemberData(nameof(ProviderAssemblyNames))]
    public void Providers_ImplementingMessagingProvider_ReferenceAbstractions(string providerAssembly)
    {
        TestResult result = Types.InAssembly(ProviderAssembly(providerAssembly))
            .That()
            .ImplementInterface(typeof(IMessagingProvider))
            .Should()
            .HaveDependencyOn("MessagingToolbox.Abstractions")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Theory]
    [MemberData(nameof(ProviderAssemblyNames))]
    public void Providers_DoNotReferenceOtherProviders(string providerAssembly)
    {
        string[] otherProviders = ProviderAssemblies
            .Select(assembly => assembly.GetName().Name!)
            .Where(name => name != providerAssembly)
            .ToArray();


        TestResult result = Types.InAssembly(ProviderAssembly(providerAssembly))
            .ShouldNot()
            .HaveDependencyOnAny(otherProviders)
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceProvidersOrBrokerSdks()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny([ProvidersNamespace, .. BrokerSdkNamespaces])
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Web_DoesNotReferenceBrokerSdks()
    {
        TestResult result = Types.InAssembly(WebAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(BrokerSdkNamespaces)
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Web_Components_DoNotReferenceInfrastructureOrProviders()
    {
        TestResult result = Types.InAssembly(WebAssembly)
            .That()
            .Inherit(typeof(ComponentBase))
            .ShouldNot()
            .HaveDependencyOnAny(ProvidersNamespace, "MessagingToolbox.Infrastructure")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Handlers_DoNotReferenceAspNetCore()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    private static Assembly ProviderAssembly(string name)
        => ProviderAssemblies.Single(assembly => assembly.GetName().Name == name);

    private static string FailingTypes(TestResult result)
        => result.FailingTypes is null
            ? string.Empty
            : string.Join(", ", result.FailingTypes.Select(type => type.FullName));
}
