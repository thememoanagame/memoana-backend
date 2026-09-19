using MemoAna.Application.Localization;
using MemoAna.Resources.Localization;
using Microsoft.Extensions.Localization;

namespace MemoAna.Infrastructure.Localization;

/// <summary>Resolves application strings from the configured RESX localization resources.</summary>
public sealed class Localizer(IStringLocalizerFactory factory) : ILocalizer
{
    private readonly IStringLocalizer localizer = factory.Create("Strings", typeof(Strings).Assembly.FullName!);

    /// <summary>Gets the localized value associated with a resource key.</summary>
    /// <param name="key">The resource key to resolve.</param>
    /// <returns>The localized resource value for the current culture.</returns>
    public string this[string key] => localizer[key];
}
