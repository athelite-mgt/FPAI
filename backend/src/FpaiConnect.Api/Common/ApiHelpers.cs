using FpaiConnect.Application.Abstractions;
using FpaiConnect.Application.Common;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace FpaiConnect.Api.Common;

public static class ApiHelpers
{
    /// <summary>Validates a DTO's data annotations, returning a ValidationProblem when it fails.</summary>
    public static IResult? Validate<T>(T model) where T : notnull
    {
        var context = new ValidationContext(model);
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(model, context, results, validateAllProperties: true))
            return null;

        var errors = results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : ["" ])
                .Select(m => (Member: m, r.ErrorMessage)))
            .GroupBy(x => x.Member)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage ?? "Invalid value").ToArray());

        return Results.ValidationProblem(errors);
    }

    public static async Task<PagedResult<TOut>> ToPagedResultAsync<TIn, TOut>(
        this IQueryable<TIn> query,
        PageQuery page,
        Func<TIn, TOut> projector,
        CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize)
            .ToListAsync(ct);

        return new PagedResult<TOut>(items.Select(projector).ToList(), page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Restricts a department-scoped query to the rows the caller may read.
    /// Staff see only their own department; heads, admins and the accountant see all.
    /// </summary>
    public static IQueryable<T> WhereReadable<T>(this IQueryable<T> query, ICurrentUser user)
        where T : class, FpaiConnect.Domain.Common.IDepartmentScoped
    {
        if (user.IsSuperAdmin
            || user.IsInRole(FpaiConnect.Domain.Entities.RoleNames.DepartmentHead)
            || user.IsInRole(FpaiConnect.Domain.Entities.RoleNames.ExternalAccountant))
            return query;

        var dept = user.DepartmentId;
        // A user with no department can see nothing rather than everything.
        return dept is null ? query.Where(_ => false) : query.Where(x => x.DepartmentId == dept);
    }

    public static IResult Forbidden(string reason) =>
        Results.Problem(title: "Forbidden", detail: reason, statusCode: StatusCodes.Status403Forbidden);

    public static IResult NotFoundProblem(string what) =>
        Results.Problem(title: "Not found", detail: what, statusCode: StatusCodes.Status404NotFound);

    public static IResult Conflict(string reason) =>
        Results.Problem(title: "Conflict", detail: reason, statusCode: StatusCodes.Status409Conflict);

    public static IResult BadRequest(string reason) =>
        Results.Problem(title: "Bad request", detail: reason, statusCode: StatusCodes.Status400BadRequest);
}
