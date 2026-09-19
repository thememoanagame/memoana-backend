namespace MemoAna.Application.Localization;

/// <summary>
/// Provides access to localized application strings by resource key.
/// </summary>
/// <remarks>
/// Implementations resolve the requested key using the current localization culture.
/// Missing-resource behavior follows the configured localization provider.
/// </remarks>
public interface ILocalizer
{
    /// <summary>
    /// Gets the localized value associated with the specified resource key.
    /// </summary>
    /// <param name="key">The resource key to resolve.</param>
    /// <returns>The localized resource value for the current culture.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the underlying localization provider rejects a null key.</exception>
    string this[string key] { get; }
}
