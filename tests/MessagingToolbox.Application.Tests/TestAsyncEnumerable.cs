namespace MessagingToolbox.Application.Tests;

/// <summary>Builds IAsyncEnumerable sequences for provider substitutes.</summary>
internal static class TestAsyncEnumerable
{
    public static async IAsyncEnumerable<T> Of<T>(params T[] items)
    {
        foreach (T item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
