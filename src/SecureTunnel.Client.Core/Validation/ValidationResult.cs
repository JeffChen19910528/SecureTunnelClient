namespace SecureTunnel.Client.Core.Validation;

/// <summary>
/// Accumulates structured validation issues for a configuration or
/// operation. A result with zero <see cref="ValidationSeverity.Error"/>
/// issues is considered valid, even if it carries warnings.
/// </summary>
public sealed class ValidationResult
{
    private readonly List<ValidationIssue> _issues = [];

    public IReadOnlyList<ValidationIssue> Issues => _issues;

    public bool IsValid => !_issues.Exists(i => i.Severity == ValidationSeverity.Error);

    public void AddError(string field, string message, string? fixHint = null) =>
        _issues.Add(new ValidationIssue(field, ValidationSeverity.Error, message, fixHint));

    public void AddWarning(string field, string message, string? fixHint = null) =>
        _issues.Add(new ValidationIssue(field, ValidationSeverity.Warning, message, fixHint));
}
