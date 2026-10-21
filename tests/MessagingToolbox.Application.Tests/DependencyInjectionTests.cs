using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace MessagingToolbox.Application.Tests;

public class DependencyInjectionTests
{
    public static TheoryData<Type> ValidatorTypes()
    {
        TheoryData<Type> data = [];
        foreach (Type type in typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && ValidatedType(type) is not null))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidatorTypes))]
    public void AddApplication_ValidatorInAssembly_IsRegisteredForItsRequest(Type validatorType)
    {
        ServiceCollection services = new();
        Type serviceType = typeof(IValidator<>).MakeGenericType(ValidatedType(validatorType)!);


        services.AddApplication(new ConfigurationBuilder().Build());


        services.ShouldContain(
            descriptor => descriptor.ServiceType == serviceType && descriptor.ImplementationType == validatorType,
            $"{validatorType.Name} is never registered, so the validation pipeline silently skips it.");
    }

    private static Type? ValidatedType(Type type)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
