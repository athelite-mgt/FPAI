using FpaiConnect.Api.Common;
using FpaiConnect.Application.Common;
using FpaiConnect.Application.Dtos;
using FpaiConnect.Domain.Entities;
using FpaiConnect.Domain.Enums;
using FpaiConnect.Infrastructure.Persistence;
using FpaiConnect.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FpaiConnect.Api.Endpoints;

/// <summary>Shared reference data: departments, clubs, players and vendors.</summary>
public static class DirectoryEndpoints
{
    public static void MapDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        MapDepartments(app);
        MapClubs(app);
        MapPlayers(app);
        MapVendors(app);
    }

    private static void MapDepartments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/departments").WithTags("Directory").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.Departments
                .OrderBy(d => d.Name)
                .Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.Description,
                    db.Users.Count(u => u.DepartmentId == d.Id && !u.IsDeleted)))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListDepartments");

        group.MapPost("/", async (
            [FromBody] DepartmentUpsertRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.Departments.AnyAsync(d => d.Code == code, ct))
                return ApiHelpers.Conflict($"A department with the code {code} already exists.");

            var department = new Department
            {
                Code = code,
                Name = request.Name.Trim(),
                Description = request.Description,
            };
            db.Departments.Add(department);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/departments/{department.Id}",
                new DepartmentDto(department.Id, department.Code, department.Name,
                    department.Description, 0));
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("CreateDepartment");

        group.MapPut("/{id:guid}", async (
            Guid id, [FromBody] DepartmentUpsertRequest request,
            AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (department is null) return ApiHelpers.NotFoundProblem("Department not found.");

            var code = request.Code.Trim().ToUpperInvariant();
            if (await db.Departments.AnyAsync(d => d.Code == code && d.Id != id, ct))
                return ApiHelpers.Conflict($"A department with the code {code} already exists.");

            // Seeded departments are referenced by code when creating welfare, legal, governance
            // and operations records, so renaming their code would break those lookups.
            if (DepartmentCodes.All.Contains(department.Code) && department.Code != code)
                return ApiHelpers.Conflict(
                    $"{department.Code} is a built-in department; its code cannot be changed. " +
                    "The display name and description can.");

            department.Code = code;
            department.Name = request.Name.Trim();
            department.Description = request.Description;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new DepartmentDto(department.Id, department.Code, department.Name,
                department.Description,
                await db.Users.CountAsync(u => u.DepartmentId == id && !u.IsDeleted, ct)));
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("UpdateDepartment");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (department is null) return ApiHelpers.NotFoundProblem("Department not found.");

            if (DepartmentCodes.All.Contains(department.Code))
                return ApiHelpers.Conflict(
                    $"{department.Name} is a built-in department and cannot be removed.");

            if (await db.Users.AnyAsync(u => u.DepartmentId == id && !u.IsDeleted, ct))
                return ApiHelpers.Conflict("Move the people in this department elsewhere first.");

            // Records are department-scoped; deleting a department with history would orphan them.
            var hasRecords =
                await db.WelfareCases.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.LegalCases.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.Vouchers.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.Expenses.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.Meetings.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.Events.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.WorkTasks.AnyAsync(x => x.DepartmentId == id, ct)
                || await db.Documents.AnyAsync(x => x.DepartmentId == id, ct);

            if (hasRecords)
                return ApiHelpers.Conflict("This department has records on file and cannot be removed.");

            db.Departments.Remove(department);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.UsersManage)
        .WithName("DeleteDepartment");
    }

    private static void MapClubs(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/clubs").WithTags("Directory").RequireAuthorization();

        group.MapGet("/", async (PageQuery page, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Clubs.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(c => c.Name.Contains(term) || (c.City != null && c.City.Contains(term)));
            }

            var result = await query
                .OrderBy(c => c.Name)
                .Select(c => new ClubDto(c.Id, c.Name, c.City, c.League,
                    db.Players.Count(p => p.CurrentClubId == c.Id)))
                .ToPagedResultAsync(page, x => x, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.DirectoryRead)
        .WithName("ListClubs");

        group.MapPost("/", async ([FromBody] ClubUpsertRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var club = new Club { Name = request.Name.Trim(), City = request.City, League = request.League };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/clubs/{club.Id}",
                new ClubDto(club.Id, club.Name, club.City, club.League, 0));
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("CreateClub");

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] ClubUpsertRequest request,
            AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (club is null) return ApiHelpers.NotFoundProblem("Club not found.");

            club.Name = request.Name.Trim();
            club.City = request.City;
            club.League = request.League;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ClubDto(club.Id, club.Name, club.City, club.League,
                await db.Players.CountAsync(p => p.CurrentClubId == club.Id, ct)));
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("UpdateClub");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (club is null) return ApiHelpers.NotFoundProblem("Club not found.");

            if (await db.Players.AnyAsync(p => p.CurrentClubId == id, ct))
                return ApiHelpers.Conflict("This club still has players assigned to it.");

            db.Clubs.Remove(club);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("DeleteClub");
    }

    private static void MapPlayers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/players").WithTags("Directory").RequireAuthorization();

        group.MapGet("/", async (
            PageQuery page,
            [FromQuery] PlayerStatus? status,
            [FromQuery] Guid? clubId,
            AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Players.AsNoTracking().Include(p => p.CurrentClub).AsQueryable();

            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(p => p.FullName.Contains(term) || p.MembershipId.Contains(term));
            }
            if (status is { } s) query = query.Where(p => p.Status == s);
            if (clubId is { } c) query = query.Where(p => p.CurrentClubId == c);

            query = page.SortBy?.ToLowerInvariant() switch
            {
                "membershipid" => page.SortDescending
                    ? query.OrderByDescending(p => p.MembershipId) : query.OrderBy(p => p.MembershipId),
                "status" => page.SortDescending
                    ? query.OrderByDescending(p => p.Status) : query.OrderBy(p => p.Status),
                _ => page.SortDescending
                    ? query.OrderByDescending(p => p.FullName) : query.OrderBy(p => p.FullName)
            };

            var result = await query
                .Select(p => new PlayerDto(p.Id, p.MembershipId, p.FullName, p.DateOfBirth, p.Position,
                    p.Nationality, p.CurrentClubId, p.CurrentClub != null ? p.CurrentClub.Name : null,
                    p.JerseyNumber, p.ContactEmail, p.ContactPhone, p.Status,
                    db.WelfareCases.Count(w => w.PlayerId == p.Id),
                    db.LegalCases.Count(l => l.PlayerId == p.Id)))
                .ToPagedResultAsync(page, x => x, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.DirectoryRead)
        .WithName("ListPlayers");

        group.MapGet("/lookup", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.Players.AsNoTracking()
                .OrderBy(p => p.FullName)
                .Select(p => new LookupDto(p.Id, p.FullName, p.MembershipId))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(Policies.DirectoryRead)
        .WithName("PlayerLookup");

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var player = await db.Players.AsNoTracking().Include(p => p.CurrentClub)
                .Where(p => p.Id == id)
                .Select(p => new PlayerDto(p.Id, p.MembershipId, p.FullName, p.DateOfBirth, p.Position,
                    p.Nationality, p.CurrentClubId, p.CurrentClub != null ? p.CurrentClub.Name : null,
                    p.JerseyNumber, p.ContactEmail, p.ContactPhone, p.Status,
                    db.WelfareCases.Count(w => w.PlayerId == p.Id),
                    db.LegalCases.Count(l => l.PlayerId == p.Id)))
                .FirstOrDefaultAsync(ct);

            return player is null ? ApiHelpers.NotFoundProblem("Player not found.") : Results.Ok(player);
        })
        .RequireAuthorization(Policies.DirectoryRead)
        .WithName("GetPlayer");

        group.MapPost("/", async ([FromBody] PlayerUpsertRequest request, AppDbContext db,
            ReferenceNumberGenerator refs, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;
            if (request.CurrentClubId is { } clubId && !await db.Clubs.AnyAsync(c => c.Id == clubId, ct))
                return ApiHelpers.BadRequest("The selected club does not exist.");

            var player = new Player
            {
                MembershipId = await refs.NextPlayerMembershipAsync(ct),
                FullName = request.FullName.Trim(),
                DateOfBirth = request.DateOfBirth,
                Position = request.Position,
                Nationality = request.Nationality,
                CurrentClubId = request.CurrentClubId,
                JerseyNumber = request.JerseyNumber,
                ContactEmail = request.ContactEmail,
                ContactPhone = request.ContactPhone,
                Status = request.Status
            };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);

            var clubName = player.CurrentClubId is null ? null
                : await db.Clubs.Where(c => c.Id == player.CurrentClubId).Select(c => c.Name).FirstOrDefaultAsync(ct);

            return Results.Created($"/api/players/{player.Id}", new PlayerDto(
                player.Id, player.MembershipId, player.FullName, player.DateOfBirth, player.Position,
                player.Nationality, player.CurrentClubId, clubName, player.JerseyNumber,
                player.ContactEmail, player.ContactPhone, player.Status, 0, 0));
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("CreatePlayer");

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] PlayerUpsertRequest request,
            AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (player is null) return ApiHelpers.NotFoundProblem("Player not found.");
            if (request.CurrentClubId is { } clubId && !await db.Clubs.AnyAsync(c => c.Id == clubId, ct))
                return ApiHelpers.BadRequest("The selected club does not exist.");

            player.FullName = request.FullName.Trim();
            player.DateOfBirth = request.DateOfBirth;
            player.Position = request.Position;
            player.Nationality = request.Nationality;
            player.CurrentClubId = request.CurrentClubId;
            player.JerseyNumber = request.JerseyNumber;
            player.ContactEmail = request.ContactEmail;
            player.ContactPhone = request.ContactPhone;
            player.Status = request.Status;
            await db.SaveChangesAsync(ct);

            var clubName = player.CurrentClubId is null ? null
                : await db.Clubs.Where(c => c.Id == player.CurrentClubId).Select(c => c.Name).FirstOrDefaultAsync(ct);

            return Results.Ok(new PlayerDto(player.Id, player.MembershipId, player.FullName,
                player.DateOfBirth, player.Position, player.Nationality, player.CurrentClubId, clubName,
                player.JerseyNumber, player.ContactEmail, player.ContactPhone, player.Status,
                await db.WelfareCases.CountAsync(w => w.PlayerId == id, ct),
                await db.LegalCases.CountAsync(l => l.PlayerId == id, ct)));
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("UpdatePlayer");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (player is null) return ApiHelpers.NotFoundProblem("Player not found.");

            if (await db.WelfareCases.AnyAsync(w => w.PlayerId == id, ct)
                || await db.LegalCases.AnyAsync(l => l.PlayerId == id, ct))
                return ApiHelpers.Conflict("This member has cases on file and cannot be removed.");

            db.Players.Remove(player);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.DirectoryWrite)
        .WithName("DeletePlayer");
    }

    private static void MapVendors(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vendors").WithTags("Directory").RequireAuthorization();

        group.MapGet("/", async (PageQuery page, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Vendors.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(page.Search))
            {
                var term = page.Search.Trim();
                query = query.Where(v => v.Name.Contains(term)
                                         || (v.GstNumber != null && v.GstNumber.Contains(term)));
            }

            var result = await query.OrderBy(v => v.Name)
                .Select(v => new VendorDto(v.Id, v.Name, v.GstNumber, v.ContactEmail, v.ContactPhone,
                    v.BankAccount, db.Vouchers.Count(x => x.VendorId == v.Id)))
                .ToPagedResultAsync(page, x => x, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization(Policies.FinanceRead)
        .WithName("ListVendors");

        group.MapPost("/", async ([FromBody] VendorUpsertRequest request, AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var vendor = new Vendor
            {
                Name = request.Name.Trim(), GstNumber = request.GstNumber,
                ContactEmail = request.ContactEmail, ContactPhone = request.ContactPhone,
                BankAccount = request.BankAccount
            };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/vendors/{vendor.Id}", new VendorDto(vendor.Id, vendor.Name,
                vendor.GstNumber, vendor.ContactEmail, vendor.ContactPhone, vendor.BankAccount, 0));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("CreateVendor");

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] VendorUpsertRequest request,
            AppDbContext db, CancellationToken ct) =>
        {
            if (ApiHelpers.Validate(request) is { } invalid) return invalid;

            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null) return ApiHelpers.NotFoundProblem("Vendor not found.");

            vendor.Name = request.Name.Trim();
            vendor.GstNumber = request.GstNumber;
            vendor.ContactEmail = request.ContactEmail;
            vendor.ContactPhone = request.ContactPhone;
            vendor.BankAccount = request.BankAccount;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new VendorDto(vendor.Id, vendor.Name, vendor.GstNumber, vendor.ContactEmail,
                vendor.ContactPhone, vendor.BankAccount,
                await db.Vouchers.CountAsync(x => x.VendorId == id, ct)));
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("UpdateVendor");

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);
            if (vendor is null) return ApiHelpers.NotFoundProblem("Vendor not found.");

            if (await db.Vouchers.AnyAsync(x => x.VendorId == id, ct))
                return ApiHelpers.Conflict("This vendor has vouchers on file and cannot be removed.");

            db.Vendors.Remove(vendor);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.FinanceWrite)
        .WithName("DeleteVendor");
    }
}
