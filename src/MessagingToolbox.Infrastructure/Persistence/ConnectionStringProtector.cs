using MessagingToolbox.Application.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace MessagingToolbox.Infrastructure.Persistence;

/// <summary>Protects connection strings at rest with ASP.NET Core Data Protection (NFR-4).</summary>
public sealed class ConnectionStringProtector : IConnectionStringProtector
{
    // Bump only with a migration strategy — existing profiles must stay readable.
    private const string Purpose = "MessagingToolbox.ConnectionStrings.v1";

    private readonly IDataProtector _protector;

    public ConnectionStringProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
