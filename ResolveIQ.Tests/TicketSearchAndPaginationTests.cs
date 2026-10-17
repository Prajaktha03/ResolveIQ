using Microsoft.AspNetCore.Mvc;
using ResolveIQ.Api.Controllers;
using ResolveIQ.Api.Data;
using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class TicketSearchAndPaginationTests
{
    private static async Task<(TicketsController controller, AppDbContext db, User employee, User employee2, User agent, User admin)> CreateSetupAsync()
    {
        var db = TestHelpers.CreateInMemoryDbContext();
        var (employee, agent, admin) = await TestHelpers.SeedDefaultUsersAsync(db);

        var employee2 = new User
        {
            Id = 10,
            FullName = "Employee Two",
            Email = "employee2@test.com",
            PasswordHash = "hashed_pw",
            Role = "Employee",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(employee2);
        await db.SaveChangesAsync();

        var slaService = new SlaService();
        var ticketService = new TicketService(db, slaService);
        var controller = new TicketsController(ticketService, db, slaService);

        return (controller, db, employee, employee2, agent, admin);
    }

    [Fact]
    public async Task Search_ByTitle_ReturnsMatchingTickets()
    {
        var (controller, db, employee, _, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "Network printer offline in Sector 4", Description = "Cannot print blueprints", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "Medium" },
            new Ticket { Id = 2, Title = "Database server high memory usage", Description = "App server latency spiked", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "High" },
            new Ticket { Id = 3, Title = "Desk printer driver setup", Description = "Need new driver installed", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "InProgress", Priority = "Low" }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        var result = await controller.GetAll(new TicketQueryParameters { Search = "printer" });
        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Ticket>>(okResult.Value);

        Assert.Equal(2, paged.TotalCount);
        Assert.Contains(paged.Items, t => t.Id == 1);
        Assert.Contains(paged.Items, t => t.Id == 3);
        Assert.DoesNotContain(paged.Items, t => t.Id == 2);
    }

    [Fact]
    public async Task Search_ByDescription_ReturnsMatchingTickets()
    {
        var (controller, db, employee, _, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "Issue A", Description = "Kernel panic observed on boot", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "High" },
            new Ticket { Id = 2, Title = "Issue B", Description = "Minor css alignment error", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "Low" }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        var result = await controller.GetAll(new TicketQueryParameters { Search = "kernel panic" });
        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Ticket>>(okResult.Value);

        Assert.Single(paged.Items);
        Assert.Equal(1, paged.Items[0].Id);
    }

    [Fact]
    public async Task Search_ByIdentifier_ReturnsExactTicketIdMatch()
    {
        var (controller, db, employee, _, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 404, Title = "Not Found Exception in WebApp", Description = "Details here", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "High" },
            new Ticket { Id = 500, Title = "Internal Server Error", Description = "Uncaught error", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id, Status = "Open", Priority = "Critical" }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        var result = await controller.GetAll(new TicketQueryParameters { Search = "500" });
        var okResult = Assert.IsType<OkObjectResult>(result);
        var paged = Assert.IsType<PagedResult<Ticket>>(okResult.Value);

        Assert.Single(paged.Items);
        Assert.Equal(500, paged.Items[0].Id);
    }

    [Fact]
    public async Task Filter_ByStatusAndPriority_AppliesFiltersCorrectly()
    {
        var (controller, db, employee, _, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "T1", CreatedByUserId = employee.Id, Status = "Open", Priority = "Critical" },
            new Ticket { Id = 2, Title = "T2", CreatedByUserId = employee.Id, Status = "InProgress", Priority = "Critical" },
            new Ticket { Id = 3, Title = "T3", CreatedByUserId = employee.Id, Status = "Open", Priority = "Medium" },
            new Ticket { Id = 4, Title = "T4", CreatedByUserId = employee.Id, Status = "Resolved", Priority = "Low" }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Filter by Status = Open
        var openResult = await controller.GetAll(new TicketQueryParameters { Status = "Open" });
        var openPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(openResult).Value);
        Assert.Equal(2, openPaged.TotalCount);
        Assert.All(openPaged.Items, t => Assert.Equal("Open", t.Status));

        // Filter by Priority = Critical
        var critResult = await controller.GetAll(new TicketQueryParameters { Priority = "Critical" });
        var critPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(critResult).Value);
        Assert.Equal(2, critPaged.TotalCount);
        Assert.All(critPaged.Items, t => Assert.Equal("Critical", t.Priority));

        // Filter by Status = Open AND Priority = Critical
        var comboResult = await controller.GetAll(new TicketQueryParameters { Status = "Open", Priority = "Critical" });
        var comboPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(comboResult).Value);
        Assert.Single(comboPaged.Items);
        Assert.Equal(1, comboPaged.Items[0].Id);
    }

    [Fact]
    public async Task Filter_ByAssigneeAndCreator_ReturnsTargetedTickets()
    {
        var (controller, db, employee, employee2, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "T1", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id },
            new Ticket { Id = 2, Title = "T2", CreatedByUserId = employee2.Id, AssignedToUserId = agent.Id },
            new Ticket { Id = 3, Title = "T3", CreatedByUserId = employee.Id, AssignedToUserId = null }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Filter by Creator
        var creatorResult = await controller.GetAll(new TicketQueryParameters { CreatorId = employee.Id });
        var creatorPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(creatorResult).Value);
        Assert.Equal(2, creatorPaged.TotalCount);
        Assert.All(creatorPaged.Items, t => Assert.Equal(employee.Id, t.CreatedByUserId));

        // Filter by Assignee
        var assigneeResult = await controller.GetAll(new TicketQueryParameters { AssigneeId = agent.Id });
        var assigneePaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(assigneeResult).Value);
        Assert.Equal(2, assigneePaged.TotalCount);
        Assert.All(assigneePaged.Items, t => Assert.Equal(agent.Id, t.AssignedToUserId));
    }

    [Fact]
    public async Task Filter_ByDateRange_FiltersByCreatedAtFromAndTo()
    {
        var (controller, db, employee, _, _, admin) = await CreateSetupAsync();

        var baseDate = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "Day 1", CreatedByUserId = employee.Id, CreatedAt = baseDate },
            new Ticket { Id = 2, Title = "Day 2", CreatedByUserId = employee.Id, CreatedAt = baseDate.AddDays(1) },
            new Ticket { Id = 3, Title = "Day 3", CreatedByUserId = employee.Id, CreatedAt = baseDate.AddDays(2) },
            new Ticket { Id = 4, Title = "Day 4", CreatedByUserId = employee.Id, CreatedAt = baseDate.AddDays(3) }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        var rangeResult = await controller.GetAll(new TicketQueryParameters
        {
            CreatedAtFrom = baseDate.AddDays(0.5),
            CreatedAtTo = baseDate.AddDays(2.5)
        });

        var paged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(rangeResult).Value);
        Assert.Equal(2, paged.TotalCount);
        Assert.Contains(paged.Items, t => t.Id == 2);
        Assert.Contains(paged.Items, t => t.Id == 3);
    }

    [Fact]
    public async Task Sorting_ByTitle_AscendingAndDescending()
    {
        var (controller, db, employee, _, _, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "Charlie", CreatedByUserId = employee.Id },
            new Ticket { Id = 2, Title = "Alpha", CreatedByUserId = employee.Id },
            new Ticket { Id = 3, Title = "Bravo", CreatedByUserId = employee.Id }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Ascending
        var ascResult = await controller.GetAll(new TicketQueryParameters { SortBy = "title", SortDirection = "asc" });
        var ascPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(ascResult).Value);
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, ascPaged.Items.Select(t => t.Title).ToArray());

        // Descending
        var descResult = await controller.GetAll(new TicketQueryParameters { SortBy = "title", SortDirection = "desc" });
        var descPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(descResult).Value);
        Assert.Equal(new[] { "Charlie", "Bravo", "Alpha" }, descPaged.Items.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task Sorting_WithTieBreaker_IsDeterministicAcrossPages()
    {
        var (controller, db, employee, _, _, admin) = await CreateSetupAsync();

        // 4 tickets with identical priority "High" but IDs 10, 20, 30, 40
        db.Tickets.AddRange(
            new Ticket { Id = 10, Title = "T10", Priority = "High", CreatedByUserId = employee.Id },
            new Ticket { Id = 40, Title = "T40", Priority = "High", CreatedByUserId = employee.Id },
            new Ticket { Id = 20, Title = "T20", Priority = "High", CreatedByUserId = employee.Id },
            new Ticket { Id = 30, Title = "T30", Priority = "High", CreatedByUserId = employee.Id }
        );
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Page 1, size 2 (asc)
        var p1Result = await controller.GetAll(new TicketQueryParameters { SortBy = "priority", SortDirection = "asc", Page = 1, PageSize = 2 });
        var p1 = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(p1Result).Value);
        Assert.Equal(new[] { 10, 20 }, p1.Items.Select(t => t.Id).ToArray());

        // Page 2, size 2 (asc)
        var p2Result = await controller.GetAll(new TicketQueryParameters { SortBy = "priority", SortDirection = "asc", Page = 2, PageSize = 2 });
        var p2 = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(p2Result).Value);
        Assert.Equal(new[] { 30, 40 }, p2.Items.Select(t => t.Id).ToArray());
    }

    [Fact]
    public async Task Pagination_BoundariesAndClamping_WorkAsExpected()
    {
        var (controller, db, employee, _, _, admin) = await CreateSetupAsync();

        for (int i = 1; i <= 25; i++)
        {
            db.Tickets.Add(new Ticket { Id = i, Title = $"Ticket {i:D2}", CreatedByUserId = employee.Id, CreatedAt = DateTime.UtcNow.AddMinutes(i) });
        }
        await db.SaveChangesAsync();

        TestHelpers.SetUserContext(controller, admin.Id, "Admin");

        // Clamping invalid page <= 0 to page 1
        var clampPage = await controller.GetAll(new TicketQueryParameters { Page = -10, PageSize = 10 });
        var clampResult = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(clampPage).Value);
        Assert.Equal(1, clampResult.Page);
        Assert.Equal(10, clampResult.PageSize);
        Assert.Equal(25, clampResult.TotalCount);
        Assert.Equal(3, clampResult.TotalPages);
        Assert.False(clampResult.HasPreviousPage);
        Assert.True(clampResult.HasNextPage);

        // Clamping pageSize > 100 to 100
        var clampMax = await controller.GetAll(new TicketQueryParameters { Page = 1, PageSize = 999 });
        var clampMaxResult = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(clampMax).Value);
        Assert.Equal(100, clampMaxResult.PageSize);
        Assert.Equal(25, clampMaxResult.Items.Count);

        // Clamping pageSize < 1 to default 10
        var clampMin = await controller.GetAll(new TicketQueryParameters { Page = 1, PageSize = 0 });
        var clampMinResult = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(clampMin).Value);
        Assert.Equal(10, clampMinResult.PageSize);
        Assert.Equal(10, clampMinResult.Items.Count);

        // Out-of-bounds page returns empty items list without crashing
        var outOfBounds = await controller.GetAll(new TicketQueryParameters { Page = 10, PageSize = 10 });
        var outResult = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(outOfBounds).Value);
        Assert.Empty(outResult.Items);
        Assert.Equal(25, outResult.TotalCount);
        Assert.True(outResult.HasPreviousPage);
        Assert.False(outResult.HasNextPage);
    }

    [Fact]
    public async Task RolePermissions_EmployeeScopedToOwnTickets_AgentScopedToAssigned()
    {
        var (controller, db, employee, employee2, agent, admin) = await CreateSetupAsync();

        db.Tickets.AddRange(
            new Ticket { Id = 1, Title = "Emp1 Ticket", CreatedByUserId = employee.Id, AssignedToUserId = agent.Id },
            new Ticket { Id = 2, Title = "Emp2 Ticket", CreatedByUserId = employee2.Id, AssignedToUserId = agent.Id }
        );
        await db.SaveChangesAsync();

        // 1. Employee only retrieves own tickets regardless of query
        TestHelpers.SetUserContext(controller, employee.Id, "Employee");
        var empResult = await controller.GetAll(new TicketQueryParameters());
        var empPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(empResult).Value);
        Assert.Single(empPaged.Items);
        Assert.Equal(1, empPaged.Items[0].Id);

        // 2. Agent calling GetAssignedTickets only retrieves tickets assigned to agent
        TestHelpers.SetUserContext(controller, agent.Id, "Agent");
        var agentAssignedResult = await controller.GetAssignedTickets(new TicketQueryParameters());
        var agentPaged = Assert.IsType<PagedResult<Ticket>>(Assert.IsType<OkObjectResult>(agentAssignedResult).Value);
        Assert.Equal(2, agentPaged.TotalCount);
        Assert.All(agentPaged.Items, t => Assert.Equal(agent.Id, t.AssignedToUserId));
    }
}
