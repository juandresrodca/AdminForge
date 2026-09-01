namespace AdminForge.Core.Results;

/// <summary>Severity colouring applied to a status line or a table cell.</summary>
public enum ResultStatus
{
    /// <summary>No colouring. The default.</summary>
    Neutral,

    /// <summary>Green. The check passed / the value is healthy.</summary>
    Ok,

    /// <summary>Amber. Works, but someone should look at it — expiring soon, weak but not broken.</summary>
    Warning,

    /// <summary>Red. Failed, missing, expired, or actively unsafe.</summary>
    Danger,

    /// <summary>Blue. Informational context that carries no judgement.</summary>
    Info,
}
