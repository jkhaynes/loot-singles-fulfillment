using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;

namespace LootSingles.UnitTests.Auth;

public class AuthenticationServiceTests
{
    [Fact]
    public async Task LoginAsync_CorrectCredentials_ReturnsSuccess()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher);

        var result = await service.LoginAsync("jsmith", "1234", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.Success, result.Outcome);
        Assert.Equal(employee.Id, result.Employee?.Id);
    }

    [Fact]
    public async Task LoginAsync_WrongPin_ReturnsInvalidCredentials()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher);

        var result = await service.LoginAsync("jsmith", "4321", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_NonexistentUsername_ReturnsInvalidCredentials()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var service = new AuthenticationService(repository, hasher);

        var result = await service.LoginAsync("ghost", "1234", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("9999")]
    public async Task LoginAsync_DeactivatedAccount_ReturnsInvalidCredentialsRegardlessOfPinCorrectness(string suppliedPin)
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        NewEmployee(repository, hasher, "jsmith", "1234", isActive: false);
        var service = new AuthenticationService(repository, hasher);

        var result = await service.LoginAsync("jsmith", suppliedPin, CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_UsernameIsCaseInsensitive_ReturnsSuccess()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher);

        var result = await service.LoginAsync("JSmith", "1234", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_Success_RecordsLoginAuditEvent()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher);

        await service.LoginAsync("jsmith", "1234", CancellationToken.None);

        var auditEvent = Assert.Single(repository.AuditEvents);
        Assert.Equal(employee.Id, auditEvent.ActorEmployeeId);
        Assert.Equal(EmployeeAuditActionType.Login, auditEvent.ActionType);
        Assert.Null(auditEvent.TargetEmployeeId);
    }

    private static Employee NewEmployee(
        FakeEmployeeRepository repository, IPinHasher hasher, string username, string pin, bool isActive)
    {
        var employee = new Employee
        {
            Id = repository.Employees.Count + 1,
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = hasher.Hash(pin),
            Role = EmployeeRole.Picker,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        repository.Employees.Add(employee);
        return employee;
    }

    private sealed class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = [];

        public List<EmployeeAuditEvent> AuditEvents { get; } = [];

        public Task<Employee?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken cancellationToken) =>
            Task.FromResult(Employees.SingleOrDefault(employee => employee.NormalizedUsername == normalizedUsername));

        public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Employees.SingleOrDefault(employee => employee.Id == id));

        public Task AddAsync(Employee employee, CancellationToken cancellationToken)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees);

        public Task AddAuditEventAsync(EmployeeAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            AuditEvents.Add(auditEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(int employeeId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EmployeeAuditEvent>>(AuditEvents
                .Where(auditEvent => auditEvent.ActorEmployeeId == employeeId || auditEvent.TargetEmployeeId == employeeId)
                .ToList());

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
