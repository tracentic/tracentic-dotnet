using System.Diagnostics;

namespace Tracentic.Sdk.Internal;

/// <summary>
/// Holds the single shared <see cref="ActivitySource"/> named "Tracentic"
/// that all SDK operations emit Activities through. The name must match
/// what is registered via <c>builder.AddSource("Tracentic")</c> in
/// <see cref="ServiceCollectionExtensions"/>.
/// </summary>
internal static class TracenticSdk
{
    internal static readonly ActivitySource ActivitySource =
        new ActivitySource("Tracentic",
            typeof(TracenticSdk).Assembly
                .GetName().Version?.ToString() ?? "1.0.0");
}
