namespace Skanyxx.Module.Identity.Entra;

/// <summary>What the id token says a person is in the tenant (<c>acct</c>, <c>idp</c>).</summary>
internal enum EntraAccountKind
{
    Member,
    Guest,

    /// <summary>No <c>acct</c> claim: the optional claim is not configured, so a guest cannot be told apart (fail closed, D9).</summary>
    Unknown
}
