using System.Reflection;
using LootSingles.Application.Import;
using LootSingles.Domain.Orders;

namespace LootSingles.UnitTests.Import;

public class PiiExclusionTests
{
    private static readonly string[] PiiTerms =
    [
        "customer",
        "recipient",
        "shipping",
        "address",
        "street",
        "email",
        "phone",
        "contact",
    ];

    [Fact]
    public void DomainAndApplicationModels_DoNotExposeSettableCustomerPii()
    {
        var exposedMembers = new[] { typeof(Order).Assembly, typeof(ImportAttempt).Assembly }
            .Distinct()
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type =>
                GetSettableMembers(type)
                    .Where(member =>
                        PiiTerms.Any(term =>
                            member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .Select(member => $"{type.FullName}.{member.Name}")
            )
            .Order()
            .ToList();

        Assert.Empty(exposedMembers);
    }

    private static IEnumerable<MemberInfo> GetSettableMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        return type.GetProperties(flags)
            .Where(property => property.SetMethod is not null)
            .Cast<MemberInfo>()
            .Concat(type.GetFields(flags).Where(field => !field.IsInitOnly));
    }
}
