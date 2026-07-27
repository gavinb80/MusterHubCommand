using MusterHubCommand.Api.Data.Entities;

namespace MusterHubCommand.Api.Contracts;

public record OrgUnitDto(Guid Id, string Name, string? Code, string OrgUnitTypeName, Guid? ParentId);

// OperatorTier is null when the employee isn't an operator at all.
public record EmployeeDto(Guid Id, string DisplayName, string? EmployeeNumber, CommandOperatorTier? OperatorTier);

public record SetOperatorRequest(CommandOperatorTier? Tier);
