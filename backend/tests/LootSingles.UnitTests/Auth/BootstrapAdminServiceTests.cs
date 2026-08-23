using LootSingles.Application.Auth;
using LootSingles.Domain.Employees;
using LootSingles.Infrastructure.Auth;

namespace LootSingles.UnitTests.Auth;

public sealed class BootstrapAdminServiceTests
{
    [Theory]
    [InlineData("", "Manager", "1234")]
    [InlineData("manager", "", "1234")]
    [InlineData("manager", "Manager", "12a4")]
    [InlineData("manager", "Manager", "123")]
    public async Task BootstrapAsync_InvalidInput_DoesNotCreateEmployee(
        string username,
        string displayName,
        string pin
    )
    {
        var repository = new FakeEmployeeRepository();
        var service = new BootstrapAdminService(repository, new Pbkdf2PinHasher());

        var result = await service.BootstrapAsync(username, displayName, pin, default);

        Assert.Equal(BootstrapAdminOutcome.InvalidInput, result);
        Assert.Empty(repository.Employees);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task BootstrapAsync_EmployeeAlreadyExists_RefusesWithoutModification()
    {
        var existing = NewEmployee("picker");
        var repository = new FakeEmployeeRepository([existing]);
        var service = new BootstrapAdminService(repository, new Pbkdf2PinHasher());

        var result = await service.BootstrapAsync("manager", "Manager", "1234", default);

        Assert.Equal(BootstrapAdminOutcome.EmployeesAlreadyExist, result);
        Assert.Same(existing, Assert.Single(repository.Employees));
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task BootstrapAsync_EmptyStore_CreatesOneUsableManagerAdmin()
    {
        var repository = new FakeEmployeeRepository();
        var hasher = new Pbkdf2PinHasher();
        var service = new BootstrapAdminService(repository, hasher);

        var result = await service.BootstrapAsync(
            "manager",
            "Fulfillment Manager",
            "1234",
            default
        );

        Assert.Equal(BootstrapAdminOutcome.Success, result);
        var employee = Assert.Single(repository.Employees);
        Assert.Equal("manager", employee.Username);
        Assert.Equal("MANAGER", employee.NormalizedUsername);
        Assert.Equal("Fulfillment Manager", employee.DisplayName);
        Assert.Equal(EmployeeRole.ManagerAdmin, employee.Role);
        Assert.True(employee.IsActive);
        Assert.False(employee.IsLocked);
        Assert.Equal(0, employee.FailedAttemptCount);
        Assert.NotEqual("1234", employee.PinHash);
        Assert.True(hasher.Verify(employee.PinHash, "1234"));
        Assert.Equal(1, repository.SaveCount);
    }

    private static Employee NewEmployee(string username) =>
        new()
        {
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = username,
            PinHash = "hash",
            Role = EmployeeRole.Picker,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FakeEmployeeRepository(IEnumerable<Employee>? employees = null)
        : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = employees?.ToList() ?? [];
        public int SaveCount { get; private set; }

        public Task<bool> TryAddFirstEmployeeAsync(
            Employee employee,
            CancellationToken cancellationToken
        )
        {
            if (Employees.Count != 0)
            {
                return Task.FromResult(false);
            }

            Employees.Add(employee);
            SaveCount++;
            return Task.FromResult(true);
        }

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

        public void Add(Employee employee) => Employees.Add(employee);

        public Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Employee>>(Employees);

        public void AddAuditEvent(EmployeeAuditEvent auditEvent) =>
            throw new InvalidOperationException("Bootstrap must not create an actor audit event.");

        public Task<IReadOnlyList<EmployeeAuditEvent>> GetAuditEventsAsync(
            int employeeId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
