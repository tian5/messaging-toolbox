namespace MessagingToolbox.Application.Core.Interfaces;

/// <summary>
/// Protects connection strings at rest.
/// </summary>
public interface IConnectionStringProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
