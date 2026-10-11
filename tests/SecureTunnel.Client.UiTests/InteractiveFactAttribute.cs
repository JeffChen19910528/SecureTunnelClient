namespace SecureTunnel.Client.UiTests;

/// <summary>
/// A fact that drives a real desktop UI. Skipped (not failed) when there is
/// no interactive window station, so headless CI does not report a false
/// failure - and does not report a false pass either: a skipped test is
/// "Not Tested", never "Passed".
/// </summary>
public sealed class InteractiveFactAttribute : FactAttribute
{
    public InteractiveFactAttribute()
    {
        if (!Environment.UserInteractive)
        {
            Skip = "Requires an interactive Windows session (UI automation).";
        }
    }
}
