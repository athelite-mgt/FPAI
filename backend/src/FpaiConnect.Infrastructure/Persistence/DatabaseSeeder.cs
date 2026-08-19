using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FpaiConnect.Infrastructure.Persistence;

/// <summary>
/// Creates roles, departments, demo accounts and a realistic working dataset.
/// Idempotent: safe to run on every start, seeds only what is missing.
/// </summary>
public class DatabaseSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<AppRole> roles,
    IConfiguration config,
    ILogger<DatabaseSeeder> logger)
{
    // Instance-scoped: Random is not thread-safe, and several hosts (for example parallel
    // test classes) can seed concurrently inside a single process.
    private readonly Random _rng = new(20250915);

    /// <summary>
    /// Roles and departments are structural configuration the app cannot run without — every
    /// authorization policy and every case/voucher/task references them. Unlike the demo
    /// dataset below, this must run on every startup regardless of Seed:Enabled, or a
    /// production deployment with demo seeding turned off silently ends up with zero roles:
    /// every subsequent AddToRoleAsync call fails, and every RequireRole policy check then
    /// locks the affected user out of the entire app.
    /// </summary>
    public async Task EnsureSystemDefaultsAsync(CancellationToken ct = default)
    {
        await SeedRolesAsync();
        await SeedDepartmentsAsync(ct);
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SeedRolesAsync();
        var departments = await SeedDepartmentsAsync(ct);
        var userMap = await SeedUsersAsync(departments, ct);

        if (await db.Players.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Operational seed data already present, skipping.");
            return;
        }

        var clubs = await SeedClubsAsync(ct);
        var players = await SeedPlayersAsync(clubs, ct);
        var vendors = await SeedVendorsAsync(ct);

        await SeedWelfareAsync(departments, players, userMap, ct);
        await SeedLegalAsync(departments, players, clubs, userMap, ct);
        await SeedFinanceAsync(departments, vendors, userMap, ct);
        await SeedGovernanceAsync(departments, userMap, ct);
        await SeedEventsAsync(departments, players, userMap, ct);
        await SeedWorkAsync(departments, userMap, ct);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded FPAI Connect demo dataset.");
    }

    private async Task SeedRolesAsync()
    {
        var descriptions = new Dictionary<string, string>
        {
            [RoleNames.SuperAdmin] = "Full system access across every department.",
            [RoleNames.DepartmentHead] = "Manages and approves records within their own department.",
            [RoleNames.Staff] = "Creates records, updates tasks and uploads documents in their department.",
            [RoleNames.ExternalAccountant] = "Read-only finance access plus review and reconciliation."
        };

        foreach (var role in RoleNames.All)
        {
            if (await roles.RoleExistsAsync(role)) continue;
            await roles.CreateAsync(new AppRole { Name = role, Description = descriptions[role] });
        }
    }

    private async Task<Dictionary<string, Department>> SeedDepartmentsAsync(CancellationToken ct)
    {
        var defs = new (string Code, string Name, string Description)[]
        {
            (DepartmentCodes.Welfare, "Player Welfare & Liaison", "Medical, contract, salary and wellbeing casework."),
            (DepartmentCodes.Legal, "Legal Affairs", "FIFA DRC, CAS, PSC and arbitration matters."),
            (DepartmentCodes.Finance, "Finance & Accounts", "Vouchers, expenses, invoices and reconciliation."),
            (DepartmentCodes.Governance, "Governance & Meetings", "Board meetings, motions and voting."),
            (DepartmentCodes.Operations, "Events & Operations", "Workshops, camps and outreach programmes."),
            (DepartmentCodes.Executive, "Executive Office", "Cross-department oversight and reporting.")
        };

        var existing = await db.Departments.IgnoreQueryFilters().ToDictionaryAsync(d => d.Code, ct);
        foreach (var (code, name, description) in defs)
        {
            if (existing.ContainsKey(code)) continue;
            var dept = new Department { Code = code, Name = name, Description = description };
            db.Departments.Add(dept);
            existing[code] = dept;
        }
        await db.SaveChangesAsync(ct);
        return existing;
    }

    private async Task<Dictionary<string, AppUser>> SeedUsersAsync(
        Dictionary<string, Department> depts, CancellationToken ct)
    {
        // Demo password is configurable so a real deployment never inherits a known credential.
        var password = config["Seed:DemoPassword"] ?? "Fpai@Connect2025!";

        var defs = new (string Email, string Name, string Role, string Dept, string Title)[]
        {
            ("admin@fpai.in", "Arjun Nair", RoleNames.SuperAdmin, DepartmentCodes.Executive, "Secretary General"),
            ("welfare.head@fpai.in", "Priya Menon", RoleNames.DepartmentHead, DepartmentCodes.Welfare, "Head of Player Welfare"),
            ("legal.head@fpai.in", "Vikram Shetty", RoleNames.DepartmentHead, DepartmentCodes.Legal, "Head of Legal Affairs"),
            ("finance.head@fpai.in", "Ananya Bose", RoleNames.DepartmentHead, DepartmentCodes.Finance, "Head of Finance"),
            ("governance.head@fpai.in", "Rahul Iyer", RoleNames.DepartmentHead, DepartmentCodes.Governance, "Company Secretary"),
            ("ops.head@fpai.in", "Neha Kulkarni", RoleNames.DepartmentHead, DepartmentCodes.Operations, "Head of Operations"),
            ("welfare.staff@fpai.in", "Sameer Khan", RoleNames.Staff, DepartmentCodes.Welfare, "Welfare Officer"),
            ("legal.staff@fpai.in", "Divya Rao", RoleNames.Staff, DepartmentCodes.Legal, "Legal Associate"),
            ("ops.staff@fpai.in", "Farhan Ali", RoleNames.Staff, DepartmentCodes.Operations, "Operations Coordinator"),
            ("accountant@external-ca.in", "Meera Krishnan", RoleNames.ExternalAccountant, DepartmentCodes.Finance, "External Chartered Accountant")
        };

        var map = new Dictionary<string, AppUser>();
        foreach (var (email, name, role, deptCode, title) in defs)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    FullName = name,
                    JobTitle = title,
                    DepartmentId = depts[deptCode].Id,
                    Status = UserStatus.Active,
                    CreatedAt = DateTime.UtcNow.AddMonths(-8)
                };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded)
                {
                    // Fail loudly: a partially seeded user set leaves the rest of the seeder
                    // referencing accounts that do not exist, which is far harder to diagnose.
                    var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                    logger.LogError("Failed to seed {Email}: {Errors}", email, errors);
                    throw new InvalidOperationException($"Could not seed the demo account {email}. {errors}");
                }
                await users.AddToRoleAsync(user, role);
            }
            map[email] = user;
        }
        return map;
    }

    private async Task<List<Club>> SeedClubsAsync(CancellationToken ct)
    {
        var clubs = new List<Club>
        {
            new() { Name = "FC Mumbai", City = "Mumbai", League = "Indian Super League" },
            new() { Name = "Bengaluru United", City = "Bengaluru", League = "Indian Super League" },
            new() { Name = "Kolkata Rangers", City = "Kolkata", League = "Indian Super League" },
            new() { Name = "Kerala Blasters FC", City = "Kochi", League = "Indian Super League" },
            new() { Name = "Goa Mariners", City = "Panaji", League = "I-League" },
            new() { Name = "Delhi Dynamos", City = "New Delhi", League = "I-League" },
            new() { Name = "Chennai Warriors", City = "Chennai", League = "I-League" },
            new() { Name = "Punjab Lions", City = "Ludhiana", League = "I-League" }
        };
        db.Clubs.AddRange(clubs);
        await db.SaveChangesAsync(ct);
        return clubs;
    }

    private async Task<List<Player>> SeedPlayersAsync(List<Club> clubs, CancellationToken ct)
    {
        var names = new[]
        {
            "Rohan Verma", "Sunil Chhetri Jr", "Manvir Singh", "Sandesh Jhingan", "Gurpreet Sandhu",
            "Anirudh Thapa", "Ashique Kuruniyan", "Lallianzuala Chhangte", "Jeakson Thounaojam", "Rahul Bheke",
            "Pritam Kotal", "Amrinder Singh", "Nikhil Poojary", "Liston Colaco", "Brandon Fernandes",
            "Udanta Singh", "Vishal Kaith", "Subhasish Bose", "Naorem Roshan", "Deepak Tangri",
            "Rahim Ali", "Suresh Wangjam", "Akash Mishra", "Ritwik Das", "Mohammed Yasir"
        };
        var positions = new[] { "Goalkeeper", "Defender", "Midfielder", "Forward" };

        var players = names.Select((name, i) => new Player
        {
            MembershipId = $"FPAI-{2019 + (i % 6)}-{1000 + i:D4}",
            FullName = name,
            DateOfBirth = new DateOnly(1994 + (i % 9), 1 + (i % 12), 1 + (i % 28)),
            Position = positions[i % positions.Length],
            Nationality = "India",
            CurrentClubId = clubs[i % clubs.Count].Id,
            JerseyNumber = 1 + (i % 30),
            ContactEmail = $"{name.Split(' ')[0].ToLowerInvariant()}{i}@players.fpai.in",
            ContactPhone = $"+91 98{_rng.Next(10000000, 99999999)}",
            Status = i % 11 == 0 ? PlayerStatus.Injured : i % 17 == 0 ? PlayerStatus.FreeAgent : PlayerStatus.Active
        }).ToList();

        db.Players.AddRange(players);
        await db.SaveChangesAsync(ct);
        return players;
    }

    private async Task<List<Vendor>> SeedVendorsAsync(CancellationToken ct)
    {
        var vendors = new List<Vendor>
        {
            new() { Name = "IndiGo Airlines", GstNumber = "07AABCI1234A1Z5", ContactEmail = "corporate@indigo.in" },
            new() { Name = "Bar & Bench LLP", GstNumber = "27AAECB5678B1Z2", ContactEmail = "accounts@barbench.in" },
            new() { Name = "ITC Grand Central", GstNumber = "27AAACI9012C1Z9", ContactEmail = "billing@itchotels.in" },
            new() { Name = "SportsMed Diagnostics", GstNumber = "29AAFCS3456D1Z7", ContactEmail = "ar@sportsmed.in" },
            new() { Name = "Kohli Print & Media", GstNumber = "07AAGCK7890E1Z3", ContactEmail = "sales@kohliprint.in" },
            new() { Name = "SecureNet IT Services", GstNumber = "29AAHCS2345F1Z1", ContactEmail = "finance@securenet.in" }
        };
        db.Vendors.AddRange(vendors);
        await db.SaveChangesAsync(ct);
        return vendors;
    }

    private async Task SeedWelfareAsync(Dictionary<string, Department> depts, List<Player> players,
        Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var dept = depts[DepartmentCodes.Welfare];
        var officers = new[] { users["welfare.head@fpai.in"].Id, users["welfare.staff@fpai.in"].Id };
        var categories = Enum.GetValues<WelfareCategory>();
        var statuses = new[]
        {
            WelfareStatus.New, WelfareStatus.UnderReview, WelfareStatus.Assigned,
            WelfareStatus.InProgress, WelfareStatus.Resolved, WelfareStatus.Closed
        };
        var titles = new Dictionary<WelfareCategory, string[]>
        {
            [WelfareCategory.Medical] = ["ACL rehabilitation support", "Second opinion on knee surgery", "Insurance claim for match injury"],
            [WelfareCategory.Contract] = ["Contract renewal advisory", "Unilateral extension clause review", "Release clause clarification"],
            [WelfareCategory.Salary] = ["Three months of unpaid wages", "Bonus withheld after promotion", "Delayed image-rights payment"],
            [WelfareCategory.MentalHealth] = ["Counselling referral request", "Post-injury wellbeing check", "Career transition support"],
            [WelfareCategory.Travel] = ["Visa assistance for away fixture", "Travel reimbursement dispute"],
            [WelfareCategory.Accommodation] = ["Club housing not provided", "Relocation allowance query"]
        };

        var cases = new List<WelfareCase>();
        for (var i = 0; i < 60; i++)
        {
            var category = categories[i % categories.Length];
            var status = statuses[i % statuses.Length];
            var opened = DateTime.UtcNow.AddDays(-_rng.Next(3, 190));
            var options = titles[category];

            var wc = new WelfareCase
            {
                CaseNumber = $"WEL/2025/{i + 101:D3}",
                DepartmentId = dept.Id,
                PlayerId = players[i % players.Count].Id,
                Category = category,
                Priority = (CasePriority)(i % 4),
                Status = status,
                Title = options[i % options.Length],
                Description = "Reported through the FPAI member helpline and triaged by the welfare desk.",
                IsDispute = i % 9 == 0,
                AssignedOfficerId = status == WelfareStatus.New ? null : officers[i % officers.Length],
                OpenedAt = opened,
                CreatedAt = opened,
                ResolvedAt = status is WelfareStatus.Resolved or WelfareStatus.Closed ? opened.AddDays(_rng.Next(5, 40)) : null,
                ClosedAt = status == WelfareStatus.Closed ? opened.AddDays(_rng.Next(41, 70)) : null,
                Resolution = status is WelfareStatus.Resolved or WelfareStatus.Closed
                    ? "Resolved with the club after FPAI mediation."
                    : null
            };
            cases.Add(wc);
        }
        db.WelfareCases.AddRange(cases);
        await db.SaveChangesAsync(ct);

        var notes = new List<WelfareCaseNote>();
        foreach (var wc in cases.Take(30))
        {
            notes.Add(new WelfareCaseNote
            {
                WelfareCaseId = wc.Id,
                Note = "Case registered and acknowledgement sent to the member.",
                StatusAtNote = WelfareStatus.New,
                AuthorId = officers[0],
                CreatedAt = wc.OpenedAt
            });
            if (wc.Status >= WelfareStatus.InProgress)
            {
                notes.Add(new WelfareCaseNote
                {
                    WelfareCaseId = wc.Id,
                    Note = "Club contacted; documents requested and follow-up scheduled.",
                    StatusAtNote = WelfareStatus.InProgress,
                    AuthorId = officers[1],
                    CreatedAt = wc.OpenedAt.AddDays(4)
                });
            }
        }
        db.WelfareCaseNotes.AddRange(notes);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedLegalAsync(Dictionary<string, Department> depts, List<Player> players,
        List<Club> clubs, Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var dept = depts[DepartmentCodes.Legal];
        var counsel = new[] { users["legal.head@fpai.in"].Id, users["legal.staff@fpai.in"].Id };
        var lawyers = new[]
        {
            ("Adv. Mehta", "Mehta & Associates"), ("Adv. Sharma", "Sharma Legal LLP"),
            ("Adv. D'Souza", "D'Souza Chambers"), ("Adv. Banerjee", "Banerjee Sports Law")
        };
        var types = Enum.GetValues<LegalCaseType>();
        var statuses = Enum.GetValues<LegalStatus>();
        var prefixes = new Dictionary<LegalCaseType, string>
        {
            [LegalCaseType.FifaDrc] = "FIFA/DRC",
            [LegalCaseType.Cas] = "CAS",
            [LegalCaseType.Psc] = "FIFA/PSC",
            [LegalCaseType.Arbitration] = "ARB"
        };

        var cases = new List<LegalCase>();
        for (var i = 0; i < 24; i++)
        {
            var type = types[i % types.Length];
            var status = statuses[i % statuses.Length];
            var filed = DateTime.UtcNow.AddDays(-_rng.Next(20, 260));
            var (lawyer, firm) = lawyers[i % lawyers.Length];

            cases.Add(new LegalCase
            {
                CaseNumber = $"{prefixes[type]}/2025/{i + 40:D3}",
                DepartmentId = dept.Id,
                PlayerId = players[(i * 3) % players.Count].Id,
                OpposingClubId = clubs[i % clubs.Count].Id,
                Type = type,
                Status = status,
                Outcome = status == LegalStatus.Closed ? (LegalOutcome)(1 + (i % 4)) : LegalOutcome.Pending,
                Priority = (CasePriority)(i % 4),
                LawyerName = lawyer,
                LawyerFirm = firm,
                AssignedCounselId = counsel[i % counsel.Length],
                Title = type switch
                {
                    LegalCaseType.FifaDrc => "Unpaid wages and contract termination with just cause",
                    LegalCaseType.Cas => "Appeal against disciplinary sanction",
                    LegalCaseType.Psc => "Training compensation dispute",
                    _ => "Domestic arbitration on image rights"
                },
                Description = "Filed on behalf of the member with supporting contractual evidence.",
                ClaimAmount = 250000m * (1 + (i % 12)),
                AwardedAmount = status == LegalStatus.Closed ? 180000m * (1 + (i % 8)) : null,
                FiledAt = filed,
                CreatedAt = filed,
                HearingDate = status >= LegalStatus.HearingScheduled ? filed.AddDays(_rng.Next(45, 120)) : null,
                DecisionDate = status >= LegalStatus.DecisionReceived ? filed.AddDays(_rng.Next(130, 200)) : null,
                ClosedAt = status == LegalStatus.Closed ? filed.AddDays(_rng.Next(200, 250)) : null
            });
        }
        db.LegalCases.AddRange(cases);
        await db.SaveChangesAsync(ct);

        var events = new List<LegalCaseEvent>();
        foreach (var lc in cases)
        {
            events.Add(new LegalCaseEvent
            {
                LegalCaseId = lc.Id, Title = "Case Filed",
                Detail = "Statement of claim submitted with annexures.",
                OccurredAt = lc.FiledAt, StatusAtEvent = LegalStatus.Filed, AuthorId = lc.AssignedCounselId
            });
            if (lc.Status >= LegalStatus.HearingScheduled)
            {
                events.Add(new LegalCaseEvent
                {
                    LegalCaseId = lc.Id, Title = "Response Received",
                    Detail = "Respondent club filed its answer.",
                    OccurredAt = lc.FiledAt.AddDays(25), StatusAtEvent = LegalStatus.HearingScheduled,
                    AuthorId = lc.AssignedCounselId
                });
            }
            if (lc.Status >= LegalStatus.DecisionReceived)
            {
                events.Add(new LegalCaseEvent
                {
                    LegalCaseId = lc.Id, Title = "Decision Received",
                    Detail = "Chamber communicated the grounds of its decision.",
                    OccurredAt = lc.DecisionDate ?? lc.FiledAt.AddDays(150),
                    StatusAtEvent = LegalStatus.DecisionReceived, AuthorId = lc.AssignedCounselId
                });
            }
        }
        db.LegalCaseEvents.AddRange(events);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFinanceAsync(Dictionary<string, Department> depts, List<Vendor> vendors,
        Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var finance = depts[DepartmentCodes.Finance];
        var head = users["finance.head@fpai.in"];
        var accountant = users["accountant@external-ca.in"];
        var deptList = depts.Values.ToList();
        var statuses = Enum.GetValues<VoucherStatus>();

        var vouchers = new List<Voucher>();
        for (var i = 0; i < 45; i++)
        {
            var amount = 25000m * (1 + (i % 24));
            var tax = Math.Round(amount * 0.18m, 2);
            var status = statuses[i % statuses.Length];
            var date = DateTime.UtcNow.AddDays(-_rng.Next(1, 170));

            vouchers.Add(new Voucher
            {
                VoucherNumber = $"V-{2200 + i}",
                DepartmentId = deptList[i % deptList.Count].Id,
                VendorId = vendors[i % vendors.Count].Id,
                Amount = amount,
                TaxAmount = tax,
                TotalAmount = amount + tax,
                Status = status,
                VoucherDate = date,
                CreatedAt = date,
                Description = "Operational disbursement raised against an approved purchase.",
                ApprovedById = status >= VoucherStatus.Approved && status != VoucherStatus.Rejected ? head.Id : null,
                ApprovedAt = status >= VoucherStatus.Approved && status != VoucherStatus.Rejected ? date.AddDays(2) : null,
                RejectionReason = status == VoucherStatus.Rejected ? "Supporting invoice did not match the purchase order." : null,
                ReconciledById = status >= VoucherStatus.Reconciled ? accountant.Id : null,
                ReconciledAt = status >= VoucherStatus.Reconciled ? date.AddDays(9) : null
            });
        }
        db.Vouchers.AddRange(vouchers);

        var expenseStatuses = Enum.GetValues<ExpenseStatus>();
        var categories = new[] { "Travel", "Legal fees", "Medical", "Venue hire", "Printing", "Software" };
        var expenses = new List<Expense>();
        for (var i = 0; i < 36; i++)
        {
            var status = expenseStatuses[i % expenseStatuses.Length];
            var date = DateTime.UtcNow.AddDays(-_rng.Next(1, 150));
            expenses.Add(new Expense
            {
                ExpenseNumber = $"EXP-{5100 + i}",
                DepartmentId = deptList[(i + 2) % deptList.Count].Id,
                Title = $"{categories[i % categories.Length]} claim for September programme",
                Category = categories[i % categories.Length],
                Amount = 8000m * (1 + (i % 15)),
                Status = status,
                IncurredOn = date,
                CreatedAt = date,
                Description = "Submitted with receipts through the member portal.",
                SubmittedById = users["ops.staff@fpai.in"].Id,
                ApprovedById = status >= ExpenseStatus.AccountantReview && status != ExpenseStatus.Rejected ? head.Id : null,
                ApprovedAt = status >= ExpenseStatus.AccountantReview && status != ExpenseStatus.Rejected ? date.AddDays(3) : null,
                RejectionReason = status == ExpenseStatus.Rejected ? "Duplicate of an earlier claim." : null
            });
        }
        db.Expenses.AddRange(expenses);
        await db.SaveChangesAsync(ct);

        var invoices = expenses.Where((_, i) => i % 2 == 0).Select((e, i) => new Invoice
        {
            InvoiceNumber = $"INV-{9000 + i}",
            VendorId = vendors[i % vendors.Count].Id,
            ExpenseId = e.Id,
            Amount = e.Amount,
            TaxAmount = Math.Round(e.Amount * 0.18m, 2),
            Status = (InvoiceStatus)(i % 4),
            IssuedOn = e.IncurredOn,
            DueDate = e.IncurredOn.AddDays(30),
            PaidOn = i % 4 == 2 ? e.IncurredOn.AddDays(21) : null
        }).ToList();
        db.Invoices.AddRange(invoices);

        var queries = vouchers.Where((_, i) => i % 11 == 0).Select(v => new AccountantQuery
        {
            VoucherId = v.Id,
            Question = "Please share the GST invoice and the approved purchase order for this voucher.",
            Status = QueryStatus.Open,
            RaisedById = accountant.Id,
            CreatedAt = v.VoucherDate.AddDays(4)
        }).ToList();
        db.AccountantQueries.AddRange(queries);

        // 12 months of income/expense actuals for the trend charts.
        var ledger = new List<LedgerEntry>();
        var now = DateTime.UtcNow;
        for (var monthsBack = 11; monthsBack >= 0; monthsBack--)
        {
            var point = now.AddMonths(-monthsBack);
            foreach (var dept in deptList)
            {
                ledger.Add(new LedgerEntry
                {
                    DepartmentId = dept.Id, FiscalYear = point.Year, Month = point.Month,
                    Direction = LedgerDirection.Expense,
                    Amount = 180000m + _rng.Next(0, 260000),
                    BudgetedAmount = 400000m
                });
            }
            ledger.Add(new LedgerEntry
            {
                DepartmentId = finance.Id, FiscalYear = point.Year, Month = point.Month,
                Direction = LedgerDirection.Income,
                Amount = 1800000m + _rng.Next(0, 700000),
                BudgetedAmount = 2000000m,
                Notes = "Membership subscriptions, sponsorship and grants."
            });
        }
        db.LedgerEntries.AddRange(ledger);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedGovernanceAsync(Dictionary<string, Department> depts,
        Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var dept = depts[DepartmentCodes.Governance];
        var chair = users["admin@fpai.in"];
        var voters = users.Values.ToList();

        var meetings = new List<Meeting>();
        for (var i = 0; i < 14; i++)
        {
            var scheduled = DateTime.UtcNow.AddDays(i < 9 ? -_rng.Next(10, 170) : _rng.Next(3, 45));
            var isPast = scheduled < DateTime.UtcNow;
            meetings.Add(new Meeting
            {
                ReferenceNumber = $"MTG-2025-{i + 21:D3}",
                DepartmentId = dept.Id,
                Title = (i % 3) switch
                {
                    0 => "Executive Committee Meeting",
                    1 => "Player Welfare Review Board",
                    _ => "Finance & Audit Committee"
                },
                Type = (MeetingType)(i % 4),
                Status = isPast ? MeetingStatus.Completed : MeetingStatus.Scheduled,
                ScheduledAt = scheduled,
                CreatedAt = scheduled.AddDays(-14),
                DurationMinutes = 60 + (i % 3) * 30,
                Location = i % 2 == 0 ? "FPAI House, Mumbai" : "Virtual",
                VideoLink = i % 2 == 0 ? null : "https://meet.fpai.in/board",
                Agenda = "1. Confirmation of previous minutes\n2. Department reports\n3. Motions for resolution\n4. Any other business",
                Minutes = isPast ? "Minutes confirmed and circulated to all attending members." : null,
                QuorumRequired = 5,
                ChairId = chair.Id
            });
        }
        db.Meetings.AddRange(meetings);
        await db.SaveChangesAsync(ct);

        var attendees = new List<MeetingAttendee>();
        foreach (var meeting in meetings)
        {
            foreach (var user in voters)
            {
                var isPast = meeting.Status == MeetingStatus.Completed;
                attendees.Add(new MeetingAttendee
                {
                    MeetingId = meeting.Id,
                    UserId = user.Id,
                    IsVotingMember = true,
                    Status = isPast
                        ? (_rng.Next(0, 10) < 8 ? AttendeeStatus.Attended : AttendeeStatus.Absent)
                        : AttendeeStatus.Invited
                });
            }
        }
        db.MeetingAttendees.AddRange(attendees);

        var motions = new List<Motion>();
        foreach (var (meeting, index) in meetings.Select((m, i) => (m, i)))
        {
            var count = 1 + (index % 3);
            for (var s = 1; s <= count; s++)
            {
                var isPast = meeting.Status == MeetingStatus.Completed;
                motions.Add(new Motion
                {
                    MeetingId = meeting.Id,
                    SequenceNumber = s,
                    Title = s switch
                    {
                        1 => "Approve the revised player welfare grant policy",
                        2 => "Ratify the appointment of external legal counsel",
                        _ => "Adopt the quarterly financial statements"
                    },
                    Description = "Resolution placed before the committee for decision.",
                    Status = isPast ? (_rng.Next(0, 10) < 7 ? MotionStatus.Passed : MotionStatus.Failed) : MotionStatus.Open,
                    VotingOpensAt = meeting.ScheduledAt,
                    VotingClosesAt = meeting.ScheduledAt.AddDays(2),
                    PassThreshold = s == 3 ? 0.667 : 0.5,
                    IsSecretBallot = s == 2
                });
            }
        }
        db.Motions.AddRange(motions);
        await db.SaveChangesAsync(ct);

        var votes = new List<Vote>();
        foreach (var motion in motions.Where(m => m.Status is MotionStatus.Passed or MotionStatus.Failed))
        {
            var favourable = motion.Status == MotionStatus.Passed;
            foreach (var user in voters)
            {
                if (_rng.Next(0, 10) < 2) continue; // some members did not vote
                var choice = favourable
                    ? (_rng.Next(0, 10) < 8 ? VoteChoice.For : VoteChoice.Against)
                    : (_rng.Next(0, 10) < 7 ? VoteChoice.Against : VoteChoice.For);
                if (_rng.Next(0, 12) == 0) choice = VoteChoice.Abstain;

                votes.Add(new Vote
                {
                    MotionId = motion.Id, UserId = user.Id, Choice = choice,
                    CastAt = motion.VotingOpensAt ?? DateTime.UtcNow
                });
            }
        }
        db.Votes.AddRange(votes);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedEventsAsync(Dictionary<string, Department> depts, List<Player> players,
        Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var dept = depts[DepartmentCodes.Operations];
        var owner = users["ops.head@fpai.in"];
        var cities = new[] { "Mumbai", "Kolkata", "Bengaluru", "Kochi", "Guwahati", "Goa", "New Delhi" };

        var events = new List<Event>();
        for (var i = 0; i < 22; i++)
        {
            var start = DateTime.UtcNow.AddDays(i < 15 ? -_rng.Next(5, 200) : _rng.Next(5, 90));
            var isPast = start < DateTime.UtcNow;
            var budget = 150000m * (1 + (i % 8));

            events.Add(new Event
            {
                ReferenceNumber = $"EVT-2025-{i + 11:D3}",
                DepartmentId = dept.Id,
                Name = ((EventType)(i % 5)) switch
                {
                    EventType.Workshop => "Player Rights & Contracts Workshop",
                    EventType.Camp => "Off-season Conditioning Camp",
                    EventType.Outreach => "Grassroots Outreach Programme",
                    EventType.Ceremony => "FPAI Annual Awards Night",
                    _ => "FPAI Members Charity Tournament"
                },
                Type = (EventType)(i % 5),
                Status = isPast ? EventStatus.Completed : (i % 3 == 0 ? EventStatus.Dispatched : EventStatus.Planned),
                Description = "Delivered under the FPAI member engagement calendar.",
                StartDate = start,
                CreatedAt = start.AddDays(-30),
                EndDate = start.AddDays(1 + (i % 3)),
                Venue = i % 2 == 0 ? "FPAI Training Centre" : "City Convention Hall",
                City = cities[i % cities.Length],
                BudgetAmount = budget,
                ActualCost = isPast ? budget * (0.75m + (decimal)_rng.NextDouble() * 0.4m) : 0m,
                ExpectedAttendees = 40 + (i % 6) * 25,
                ActualAttendees = isPast ? 35 + (i % 6) * 22 : 0,
                OwnerId = owner.Id
            });
        }
        db.Events.AddRange(events);
        await db.SaveChangesAsync(ct);

        var participants = new List<EventParticipant>();
        foreach (var evt in events.Take(12))
        {
            foreach (var player in players.OrderBy(_ => _rng.Next()).Take(8))
            {
                participants.Add(new EventParticipant
                {
                    EventId = evt.Id, PlayerId = player.Id,
                    Status = evt.Status == EventStatus.Completed
                        ? (_rng.Next(0, 10) < 8 ? AttendeeStatus.Attended : AttendeeStatus.Absent)
                        : AttendeeStatus.Invited
                });
            }
        }
        db.EventParticipants.AddRange(participants);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedWorkAsync(Dictionary<string, Department> depts,
        Dictionary<string, AppUser> users, CancellationToken ct)
    {
        var deptList = depts.Values.ToList();
        var staff = users.Values.ToList();
        var statuses = Enum.GetValues<WorkTaskStatus>();

        var tasks = new List<WorkTask>();
        var taskTitles = new[]
        {
            "Collect medical reports for the pending welfare case",
            "Prepare hearing bundle for the DRC submission",
            "Reconcile September travel vouchers",
            "Circulate the draft minutes for confirmation",
            "Confirm venue booking for the Kolkata workshop",
            "Update the member contact directory",
            "Chase the outstanding invoice from the vendor",
            "Draft the quarterly welfare summary for the board"
        };

        for (var i = 0; i < 40; i++)
        {
            var status = statuses[i % statuses.Length];
            var created = DateTime.UtcNow.AddDays(-_rng.Next(1, 90));
            tasks.Add(new WorkTask
            {
                ReferenceNumber = $"TSK-{4100 + i}",
                DepartmentId = deptList[i % deptList.Count].Id,
                Title = taskTitles[i % taskTitles.Length],
                Description = "Actioned from the departmental work queue.",
                Status = status,
                Priority = (CasePriority)(i % 4),
                CreatedAt = created,
                DueDate = created.AddDays(_rng.Next(3, 30)),
                CompletedAt = status == WorkTaskStatus.Done ? created.AddDays(_rng.Next(1, 20)) : null,
                AssigneeId = staff[i % staff.Count].Id
            });
        }
        db.WorkTasks.AddRange(tasks);

        var approvals = new List<ApprovalRequest>();
        for (var i = 0; i < 25; i++)
        {
            var status = i < 12 ? ApprovalStatus.Pending : (ApprovalStatus)(1 + (i % 2));
            var created = DateTime.UtcNow.AddDays(-_rng.Next(1, 45));
            approvals.Add(new ApprovalRequest
            {
                ReferenceNumber = $"APR-{7100 + i}",
                DepartmentId = deptList[i % deptList.Count].Id,
                Title = i % 2 == 0 ? "Voucher approval request" : "Expense claim approval request",
                Description = "Routed to the department head for a decision.",
                Status = status,
                EntityType = i % 2 == 0 ? "Voucher" : "Expense",
                EntityId = Guid.CreateVersion7(),
                Amount = 30000m * (1 + (i % 10)),
                CreatedAt = created,
                RequestedById = staff[i % staff.Count].Id,
                DecidedById = status == ApprovalStatus.Pending ? null : users["admin@fpai.in"].Id,
                DecidedAt = status == ApprovalStatus.Pending ? null : created.AddDays(2),
                DecisionComment = status switch
                {
                    ApprovalStatus.Approved => "Approved; within the departmental budget.",
                    ApprovalStatus.Rejected => "Rejected; please resubmit with the vendor invoice attached.",
                    _ => null
                }
            });
        }
        db.ApprovalRequests.AddRange(approvals);
        await db.SaveChangesAsync(ct);
    }
}
