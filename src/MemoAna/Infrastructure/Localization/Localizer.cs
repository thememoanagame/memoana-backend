using MemoAna.Application.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.VisualBasic;

namespace MemoAna.Infrastructure.Localization;

public sealed class Localizer(IStringLocalizerFactory factory) : ILocalizer
{
    private readonly IStringLocalizer localizer = factory.Create("Strings", typeof(Strings).Assembly.FullName!);
    public string this[string key] => localizer[key];

}
