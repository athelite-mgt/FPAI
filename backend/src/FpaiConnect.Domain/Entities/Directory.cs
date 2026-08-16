using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Enums;

namespace FpaiConnect.Domain.Entities;

public class Club : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? League { get; set; }

    public ICollection<Player> Players { get; set; } = [];
}

/// <summary>An FPAI member. Shared by the welfare and legal modules.</summary>
public class Player : BaseEntity
{
    public string MembershipId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Position { get; set; }
    public string Nationality { get; set; } = "India";
    public Guid? CurrentClubId { get; set; }
    public Club? CurrentClub { get; set; }
    public int? JerseyNumber { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public PlayerStatus Status { get; set; } = PlayerStatus.Active;

    public ICollection<WelfareCase> WelfareCases { get; set; } = [];
    public ICollection<LegalCase> LegalCases { get; set; } = [];
}

public class Vendor : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? BankAccount { get; set; }

    public ICollection<Voucher> Vouchers { get; set; } = [];
}
