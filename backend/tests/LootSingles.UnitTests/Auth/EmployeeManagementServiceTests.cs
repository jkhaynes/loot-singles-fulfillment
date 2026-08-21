using LootSingles.Application.Auth;
using LootSingles.Application.Persistence;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;

namespace LootSingles.UnitTests.Auth;

public class EmployeeManagementServiceTests
{
    [Fact]
    public async Task CreateAsync_CaseInsensitiveDuplicate_ReturnsUsernameTaken()
    {
        var repository = new FakeEmployeeRepository();
        repository.Employees.Add(NewEmployee(1, "jsmith", "hash"));
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        var result = await service.CreateAsync(9, "JSmith", "Jamie", "1234", EmployeeRole.Picker, default);

        Assert.Equal(EmployeeManagementOutcome.UsernameTaken, result.Outcome);
        Assert.Single(repository.Employees);
        Assert.Empty(repository.AuditEvents);
    }

    [Theory]
    [InlineData("", "1234")]
    [InlineData("   ", "1234")]
    [InlineData("jsmith", "12a4")]
    [InlineData("jsmith", "123")]
    [InlineData("jsmith", "")]
    public async Task CreateAsync_InvalidUsernameOrPin_ReturnsInvalidRequestWithoutPersisting(
        string username, string initialPin)
    {
        var repository = new FakeEmployeeRepository();
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        var result = await service.CreateAsync(9, username, "Jamie", initialPin, EmployeeRole.Picker, default);

        Assert.Equal(EmployeeManagementOutcome.InvalidRequest, result.Outcome);
        Assert.Empty(repository.Employees);
        Assert.Empty(repository.AuditEvents);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentDuplicateUsername_ReturnsUsernameTaken()
    {
        var repository = new FakeEmployeeRepository { ThrowDuplicateUsernameOnNextSave = true };
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        var result = await service.CreateAsync(9, "jsmith", "Jamie", "1234", EmployeeRole.Picker, default);

        Assert.Equal(EmployeeManagementOutcome.UsernameTaken, result.Outcome);
    }

    [Fact]
    public async Task CreateAsync_NewEmployee_HashesPinAndRecordsAuditEvent()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var service = new EmployeeManagementService(repository, hasher);

        var result = await service.CreateAsync(9, "jsmith", "Jamie", "1234", EmployeeRole.Picker, default);

        Assert.Equal(EmployeeManagementOutcome.Success, result.Outcome);
        Assert.NotNull(result.Employee);
        Assert.NotEqual("1234", result.Employee.PinHash);
        Assert.True(hasher.Verify(result.Employee.PinHash, "1234"));
        AssertAudit(repository, EmployeeAuditActionType.AccountCreated, 9, result.Employee.Id);
    }

    [Fact]
    public async Task DeactivateAndReactivateAsync_ToggleActiveAndReactivationClearsLockout()
    {
        var repository = new FakeEmployeeRepository();
        var employee = NewEmployee(1, "jsmith", "hash");
        employee.IsLocked = true;
        employee.FailedAttemptCount = 5;
        repository.Employees.Add(employee);
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        Assert.Equal(EmployeeManagementOutcome.Success, (await service.DeactivateAsync(9, 1, default)).Outcome);
        Assert.False(employee.IsActive);
        Assert.Equal(EmployeeManagementOutcome.Success, (await service.ReactivateAsync(9, 1, default)).Outcome);
        Assert.True(employee.IsActive);
        Assert.False(employee.IsLocked);
        Assert.Equal(0, employee.FailedAttemptCount);
        Assert.Equal(
            [EmployeeAuditActionType.Deactivated, EmployeeAuditActionType.Reactivated],
            repository.AuditEvents.Select(item => item.ActionType));
    }

    [Fact]
    public async Task ResetPinAsync_ReplacesHashWithoutChangingLockState()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(1, "jsmith", hasher.Hash("1234"));
        employee.IsLocked = true;
        employee.FailedAttemptCount = 5;
        repository.Employees.Add(employee);
        var service = new EmployeeManagementService(repository, hasher);

        var result = await service.ResetPinAsync(9, 1, "4321", default);

        Assert.Equal(EmployeeManagementOutcome.Success, result.Outcome);
        Assert.True(hasher.Verify(employee.PinHash, "4321"));
        Assert.True(employee.IsLocked);
        Assert.Equal(5, employee.FailedAttemptCount);
        AssertAudit(repository, EmployeeAuditActionType.PinReset, 9, 1);
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("")]
    public async Task ResetPinAsync_InvalidPin_ReturnsInvalidRequestWithoutChangingHash(string newPin)
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(1, "jsmith", hasher.Hash("1234"));
        repository.Employees.Add(employee);
        var service = new EmployeeManagementService(repository, hasher);

        var result = await service.ResetPinAsync(9, 1, newPin, default);

        Assert.Equal(EmployeeManagementOutcome.InvalidRequest, result.Outcome);
        Assert.True(hasher.Verify(employee.PinHash, "1234"));
        Assert.Empty(repository.AuditEvents);
    }

    [Fact]
    public async Task UnlockAsync_ClearsLockoutWithoutChangingPin()
    {
        var repository = new FakeEmployeeRepository();
        var employee = NewEmployee(1, "jsmith", "original-hash");
        employee.IsLocked = true;
        employee.FailedAttemptCount = 5;
        repository.Employees.Add(employee);
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        var result = await service.UnlockAsync(9, 1, default);

        Assert.Equal(EmployeeManagementOutcome.Success, result.Outcome);
        Assert.False(employee.IsLocked);
        Assert.Equal(0, employee.FailedAttemptCount);
        Assert.Equal("original-hash", employee.PinHash);
        AssertAudit(repository, EmployeeAuditActionType.Unlocked, 9, 1);
    }

    [Fact]
    public async Task StateChangeForMissingEmployee_ReturnsNotFoundWithoutAuditEvent()
    {
        var repository = new FakeEmployeeRepository();
        var service = new EmployeeManagementService(repository, new Pbkdf2PinHasher());

        var result = await service.DeactivateAsync(9, 404, default);

        Assert.Equal(EmployeeManagementOutcome.NotFound, result.Outcome);
        Assert.Empty(repository.AuditEvents);
    }

    private static Employee NewEmployee(int id, string username, string hash) => new()
    {
        Id = id,
        Username = username,
        NormalizedUsername = username.ToUpperInvariant(),
        DisplayName = username,
        PinHash = hash,
        Role = EmployeeRole.Picker,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static void AssertAudit(
        FakeEmployeeRepository repository, EmployeeAuditActionType action, int actorId, int targetId)
    {
        var auditEvent = Assert.Single(repository.AuditEvents);
        Assert.Equal(action, auditEvent.ActionType);
        Assert.Equal(actorId, auditEvent.ActorEmployeeId);
        Assert.Equal(targetId, auditEvent.TargetEmployeeId);
    }

    private sealed class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = [];
        public List<EmployeeAuditEvent> AuditEvents { get; } = [];
        public bool ThrowDuplicateUsernameOnNextSave { get; set; }

        public Task<Employee?> GetByNormalizedUsernameAsync(string normalizedUsername, CancellationToken token) =>
            Task.FromResult(Employees.SingleOrDefault(item => item.NormalizedUsername == normalizedUsername));
        public Task<Employee?> GetByIdAsync(int id, CancellationToken token) =>
            Task.FromResult(Employees.SingleOrDefault(item => item.Id == id));
        public Task<bool> ExistsAsync(int id, CancellationToken token) =>
            Task.FromResult(Employees.Any(item => item.Id == id));
        public void Add(Employee employee)
        {
            employee.Id = Employees.Count + 1;
            Employees.Add(employee);
        }
        public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees);
        public void AddAuditEvent(EmployeeAuditEvent auditEvent)
        {
            AuditEvents.Add(auditEvent);
        }
        public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(int employeeId, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<EmployeeAuditEvent>>(AuditEvents);
        public Task SaveChangesAsync(CancellationToken token)
        {
            if (ThrowDuplicateUsernameOnNextSave)
            {
                ThrowDuplicateUsernameOnNextSave = false;
                throw new UniqueConstraintViolationException(
                    "Username is already in use.", new InvalidOperationException("simulated unique-index violation"));
            }

            return Task.CompletedTask;
        }
    }
}
