using System.Reflection;
using Mediator;
using MessagingToolbox.Abstractions;
using MessagingToolbox.Application.Features.Connections.Queries.GetConnections;
using MessagingToolbox.Domain.Entities;
using MessagingToolbox.Infrastructure.Persistence;
using MessagingToolbox.Providers.AzureServiceBus;
using MessagingToolbox.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Architecture.Tests;

/// <summary>
/// Enforces the dependency rules of architecture doc §3 plus the DevOps amendment:
/// Abstractions is dependency-free, Domain/Application stay SDK-free, Providers reference
/// Abstractions (never the other way), Web components never bypass the Application layer.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(IMessagingProvider).Assembly;
    private static readonly Assembly DomainAssembly = typeof(EntityBase).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(GetConnectionsQuery).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ToolboxDbContext).Assembly;
    private static readonly Assembly ProvidersAssembly = typeof(AzureServiceBusProvider).Assembly;
    private static readonly Assembly WebAssembly = typeof(Home).Assembly;

    [Fact]
    public void Abstractions_DoesNotDependOnOtherLayers()
    {
        TestResult result = Types.InAssembly(AbstractionsAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "MessagingToolbox.Domain",
                "MessagingToolbox.Application",
                "MessagingToolbox.Infrastructure",
                "MessagingToolbox.Providers.AzureServiceBus",
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
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Azure.Messaging.ServiceBus",
                "Mediator")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureProvidersOrSdks()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "MessagingToolbox.Infrastructure",
                "MessagingToolbox.Providers.AzureServiceBus",
                "Microsoft.AspNetCore",
                "Azure.Messaging.ServiceBus")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Providers_ImplementingMessagingProvider_ReferenceAbstractions()
    {
        TestResult result = Types.InAssembly(ProvidersAssembly)
            .That()
            .ImplementInterface(typeof(IMessagingProvider))
            .Should()
            .HaveDependencyOn("MessagingToolbox.Abstractions")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Infrastructure_DoesNotReferenceBrokerSdks()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("Azure.Messaging.ServiceBus")
            .GetResult();


        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    [Fact]
    public void Web_DoesNotReferenceBrokerSdks()
    {
        TestResult result = Types.InAssembly(WebAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Azure.Messaging.ServiceBus", "Azure.Core")
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
            .HaveDependencyOnAny("MessagingToolbox.Providers.AzureServiceBus", "MessagingToolbox.Infrastructure")
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

    private static string FailingTypes(TestResult result)
        => result.FailingTypes is null
            ? string.Empty
            : string.Join(", ", result.FailingTypes.Select(type => type.FullName));
}
