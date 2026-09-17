namespace AccessRequestApp.Services;

/// <summary>Thrown when a decision is attempted on a request that is not Pending, or the request does not exist.</summary>
public sealed class InvalidAccessRequestTransitionException(string message) : Exception(message);
