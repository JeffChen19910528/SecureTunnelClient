using System.Windows.Data;
using System.Windows.Markup;

namespace SecureTunnel.Connect.Localization;

/// <summary>XAML usage: <c>Text="{loc:Loc Key_Name}"</c> - re-evaluates when the language changes.</summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new System.Windows.Data.Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
