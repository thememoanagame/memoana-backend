namespace MemoAna.Application.Localization;

public interface ILocalizer
{
    string this[string key] { get; }
}