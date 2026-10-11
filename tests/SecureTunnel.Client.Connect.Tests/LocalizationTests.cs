using SecureTunnel.Connect.Localization;
using Xunit;

namespace SecureTunnel.Client.Connect.Tests;

[Collection("Localization")]
public sealed class LocalizationTests : IDisposable
{
    public LocalizationTests() => Loc.Instance.Language = Loc.English;

    public void Dispose() => Loc.Instance.Language = Loc.English;

    [Fact]
    public void EveryEnglishKey_HasAChineseTranslation_AndViceVersa()
    {
        Assert.Empty(Strings.En.Keys.Except(Strings.ZhTw.Keys));
        Assert.Empty(Strings.ZhTw.Keys.Except(Strings.En.Keys));
    }

    [Fact]
    public void FormatPlaceholders_MatchBetweenLanguages()
    {
        foreach (var (key, english) in Strings.En)
        {
            Assert.Equal(
                System.Text.RegularExpressions.Regex.Matches(english, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v),
                System.Text.RegularExpressions.Regex.Matches(Strings.ZhTw[key], @"\{\d+\}").Select(m => m.Value).OrderBy(v => v));
        }
    }

    [Fact]
    public void Toggle_SwitchesLanguage_AndRaisesLanguageChanged()
    {
        var raised = 0;
        Loc.Instance.LanguageChanged += (_, _) => raised++;

        Loc.Instance.Toggle();

        Assert.Equal(Loc.TraditionalChinese, Loc.Instance.Language);
        Assert.Equal("連線", Loc.T("Button_Connect"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void UnknownKey_FallsBackToTheKeyItself()
    {
        Assert.Equal("No_Such_Key", Loc.T("No_Such_Key"));
    }
}
