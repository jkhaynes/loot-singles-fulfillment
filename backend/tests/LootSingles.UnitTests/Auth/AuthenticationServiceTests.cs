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
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

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
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("jsmith", "4321", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_NonexistentUsername_ReturnsInvalidCredentials()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("ghost", "1234", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("9999")]
    public async Task LoginAsync_DeactivatedAccount_ReturnsInvalidCredentialsRegardlessOfPinCorrectness(
        string suppliedPin
    )
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        NewEmployee(repository, hasher, "jsmith", "1234", isActive: false);
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("jsmith", suppliedPin, CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("9999")]
    public async Task LoginAsync_DeactivatedAndLockedAccount_ReturnsInvalidCredentialsNotAccountLocked(
        string suppliedPin
    )
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: false);
        employee.IsLocked = true;
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("jsmith", suppliedPin, CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_UsernameIsCaseInsensitive_ReturnsSuccess()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("JSmith", "1234", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.Success, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_Success_RecordsLoginAuditEvent()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        await service.LoginAsync("jsmith", "1234", CancellationToken.None);

        var auditEvent = Assert.Single(repository.AuditEvents);
        Assert.Equal(employee.Id, auditEvent.ActorEmployeeId);
        Assert.Equal(EmployeeAuditActionType.Login, auditEvent.ActionType);
        Assert.Null(auditEvent.TargetEmployeeId);
    }

    [Fact]
    public async Task LoginAsync_WrongPin_IncrementsFailedAttemptCount()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(
            repository,
            hasher,
            new LockoutOptions { FailedAttemptThreshold = 3 }
        );

        await service.LoginAsync("jsmith", "9999", CancellationToken.None);

        Assert.Equal(1, employee.FailedAttemptCount);
        Assert.False(employee.IsLocked);
    }

    [Fact]
    public async Task LoginAsync_WrongPinReachesThreshold_LocksAccountButStillReturnsInvalidCredentialsForThatAttempt()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(
            repository,
            hasher,
            new LockoutOptions { FailedAttemptThreshold = 3 }
        );

        AuthenticationResult? result = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            result = await service.LoginAsync("jsmith", "9999", CancellationToken.None);
        }

        Assert.Equal(AuthenticationOutcome.InvalidCredentials, result!.Outcome);
        Assert.True(employee.IsLocked);
        Assert.Equal(3, employee.FailedAttemptCount);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("9999")]
    public async Task LoginAsync_LockedAccount_ReturnsAccountLockedRegardlessOfPinCorrectness(
        string suppliedPin
    )
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        employee.IsLocked = true;
        var service = new AuthenticationService(repository, hasher, DefaultLockoutOptions());

        var result = await service.LoginAsync("jsmith", suppliedPin, CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.AccountLocked, result.Outcome);
    }

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_ResetsFailedAttemptCount()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var employee = NewEmployee(repository, hasher, "jsmith", "1234", isActive: true);
        var service = new AuthenticationService(
            repository,
            hasher,
            new LockoutOptions { FailedAttemptThreshold = 5 }
        );
        await service.LoginAsync("jsmith", "9999", CancellationToken.None);
        await service.LoginAsync("jsmith", "9999", CancellationToken.None);
        Assert.Equal(2, employee.FailedAttemptCount);

        await service.LoginAsync("jsmith", "1234", CancellationToken.None);

        Assert.Equal(0, employee.FailedAttemptCount);
    }

    private static LockoutOptions DefaultLockoutOptions() => new();

    private static Employee NewEmployee(
        FakeEmployeeRepository repository,
        IPinHasher hasher,
        string username,
        string pin,
        bool isActive
    )
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

        public Task<Employee?> GetByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                Employees.SingleOrDefault(employee =>
                    employee.NormalizedUsername == normalizedUsername
                )
            );

        public Task<Employee?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Employees.SingleOrDefault(employee => employee.Id == id));

        public Task<bool> ExistsAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Employees.Any(employee => employee.Id == id));

        public void Add(Employee employee)
        {
            Employees.Add(employee);
        }

        public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees);

        public void AddAuditEvent(EmployeeAuditEvent auditEvent)
        {
            AuditEvents.Add(auditEvent);
        }

        public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult<IReadOnlyList<EmployeeAuditEvent>>(
                AuditEvents
                    .Where(auditEvent =>
                        auditEvent.ActorEmployeeId == employeeId
                        || auditEvent.TargetEmployeeId == employeeId
                    )
                    .ToList()
            );

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
