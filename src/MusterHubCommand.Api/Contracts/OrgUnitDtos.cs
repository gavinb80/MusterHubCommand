namespace MusterHubCommand.Api.Contracts;

public record OrgUnitDto(Guid Id, string Name, string? Code, string OrgUnitTypeName, Guid? ParentId);

public record EmployeeDto(Guid Id, string DisplayName, string? EmployeeNumber, bool IsOperator);
