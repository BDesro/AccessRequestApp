namespace AccessRequestApp.Services;

/// <summary>Thrown when a decision is attempted on a request that is not Pending, or the request does not exist.</summary>
public sealed class InvalidAccessRequestTransitionException(string message) : Exception(message);

/// <summary>Thrown when an administrator attempts to approve or deny a request they submitted themselves.</summary>
public sealed class SelfDecisionNotAllowedException(string message) : Exception(message);
