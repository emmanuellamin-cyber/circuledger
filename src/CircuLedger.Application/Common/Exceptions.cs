namespace CircuLedger.Application.Common;

public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

public sealed class ConflictException(string message, string code = "conflict") : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class ForbiddenAccessException(string message) : Exception(message);
