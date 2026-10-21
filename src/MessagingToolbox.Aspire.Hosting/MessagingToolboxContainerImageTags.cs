using System.Reflection;

namespace MessagingToolbox.Aspire.Hosting;

/// <summary>Container image coordinates of the published toolbox image (ghcr.io only, per DevOps guidelines §1).</summary>
internal static class MessagingToolboxContainerImageTags
{
    public const string Registry = "ghcr.io";

    public const string Image = "tian5/messaging-toolbox";

    /// <summary>Prerelease channel, published from every main push (DevOps guidelines §3).</summary>
    public const string EdgeTag = "edge";

    internal const string ImageTagMetadataKey = "ContainerImageTag";

    /// <summary>
    /// The image tag matching this package: a package built from a <c>v*</c> tag carries its
    /// version (stamped by the project file), which the publish workflow also pushes as an image
    /// tag, so a pinned NuGet pulls the matching image. Untagged builds follow <see cref="EdgeTag"/>.
    /// </summary>
    public static string Tag { get; } = typeof(MessagingToolboxContainerImageTags).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == ImageTagMetadataKey)?.Value is { Length: > 0 } version
            ? version
            : EdgeTag;
}
