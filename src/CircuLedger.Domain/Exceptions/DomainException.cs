namespace CircuLedger.Domain.Exceptions;

/// <summary>A business rule was violated. Mapped to HTTP 422 by the API.</summary>
public class DomainException(string message, string code = "business_rule_violation") : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>A fund does not hold enough money for a debit. Mapped to HTTP 409.</summary>
public sealed class InsufficientFundsException(string fundName, decimal balance, decimal requested)
    : DomainException($"{fundName} balance (SLE {balance:N2}) is insufficient for SLE {requested:N2}.", "insufficient_funds")
{
    public decimal Balance { get; } = balance;
    public decimal Requested { get; } = requested;
}

public sealed class InvalidStateTransitionException(string entity, object from, object to)
    : DomainException($"{entity} cannot move from {from} to {to}.", "invalid_state_transition");
