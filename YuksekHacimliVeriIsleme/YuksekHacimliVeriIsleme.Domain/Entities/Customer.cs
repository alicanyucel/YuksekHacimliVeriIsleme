using YuksekHacimliVeriIsleme.Domain.Abstractions;

namespace YuksekHacimliVeriIsleme.Domain.Entities;

public sealed class Customer : Entity
{
    public string CustomerId { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string NationalId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateOnly BirthDate { get; set; }
    public DateTime CreatedDate { get; set; }
}
