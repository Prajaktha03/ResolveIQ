using ResolveIQ.Api.Models;
using ResolveIQ.Api.Services;
using Xunit;

namespace ResolveIQ.Tests;

public class SlaServiceTests
{
    private readonly DateTime _baseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Critical", 15, 240)]
    [InlineData("High", 60, 480)]
    [InlineData("Medium", 240, 1440)]
    [InlineData("Low", 480, 4320)]
    public void GetTargets_ReturnsCorrectDurations(string priority, int expectedFirstResponseMinutes, int expectedResolutionMinutes)
    {
        var service = new SlaService();

        var targets = service.GetTargets(priority);

        Assert.Equal(TimeSpan.FromMinutes(expectedFirstResponseMinutes), targets.FirstResponse);
        Assert.Equal(TimeSpan.FromMinutes(expectedResolutionMinutes), targets.Resolution);
    }

    [Fact]
    public void GetTargets_InvalidPriority_ThrowsArgumentException()
    {
        var service = new SlaService();
        Assert.Throws<ArgumentException>(() => service.GetTargets("SuperUrgent"));
    }

    [Fact]
    public void CalculateDeadlines_CalculatesFromGivenCreatedAt()
    {
        var service = new SlaService();
        var createdAt = new DateTime(2026, 5, 10, 8, 30, 0, DateTimeKind.Utc);

        var deadlines = service.CalculateDeadlines("High", createdAt);

        Assert.Equal(createdAt.AddHours(1), deadlines.FirstResponseDueAt);
        Assert.Equal(createdAt.AddHours(8), deadlines.ResolutionDueAt);
    }

    [Fact]
    public void GetStatus_WhenFirstResponseCompletedOnTime_ReturnsMet()
    {
        var timeProvider = new TestTimeProvider(_baseTime);
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "InProgress",
            FirstResponseDueAt = _baseTime.AddHours(1),
            FirstRespondedAt = _baseTime.AddMinutes(30),
            ResolutionDueAt = _baseTime.AddHours(8)
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("Met", status.FirstResponseStatus);
    }

    [Fact]
    public void GetStatus_WhenFirstResponseCompletedLate_ReturnsBreached()
    {
        var timeProvider = new TestTimeProvider(_baseTime);
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "InProgress",
            FirstResponseDueAt = _baseTime.AddHours(1),
            FirstRespondedAt = _baseTime.AddHours(1).AddMinutes(5), // 5 min late
            ResolutionDueAt = _baseTime.AddHours(8)
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("Breached", status.FirstResponseStatus);
    }

    [Fact]
    public void GetStatus_WhenUncompletedAndMoreThanOneHourRemaining_ReturnsOnTrack()
    {
        var timeProvider = new TestTimeProvider(_baseTime);
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddHours(3), // 3 hours remaining
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(24)
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("On Track", status.FirstResponseStatus);
        Assert.Equal("On Track", status.ResolutionStatus);
    }

    [Fact]
    public void GetStatus_WhenUncompletedAndWithinOneHour_ReturnsAtRisk()
    {
        var timeProvider = new TestTimeProvider(_baseTime);
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddMinutes(45), // 45 minutes remaining (<= 1 hour)
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(24)
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("At Risk", status.FirstResponseStatus);
        Assert.Equal("On Track", status.ResolutionStatus);
    }

    [Fact]
    public void GetStatus_WhenUncompletedAndPastDeadline_ReturnsBreached()
    {
        var timeProvider = new TestTimeProvider(_baseTime.AddHours(2)); // Current time is 2 hours later
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Open",
            FirstResponseDueAt = _baseTime.AddHours(1), // Deadline was 1 hour ago
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(8)
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("Breached", status.FirstResponseStatus);
    }

    [Fact]
    public void GetStatus_TicketResolvedWithoutFirstResponse_MustNeverBeMetOrAtRisk_ReturnsBreached()
    {
        // Even if evaluated before FirstResponseDueAt, if ticket is resolved without first response,
        // it must NOT be marked Met, At Risk, or On Track. It is Breached.
        var timeProvider = new TestTimeProvider(_baseTime.AddMinutes(10));
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Resolved",
            CreatedAt = _baseTime,
            FirstResponseDueAt = _baseTime.AddMinutes(15), // Still in future when resolved at 10 min
            FirstRespondedAt = null,                       // NEVER responded
            ResolutionDueAt = _baseTime.AddHours(4),
            ResolvedAt = _baseTime.AddMinutes(10)
        };

        var status = service.GetStatus(ticket);

        // Milestone 3 requirement: A ticket resolved without a recorded first response
        // must not be incorrectly marked as having met the first-response SLA.
        Assert.Equal("Breached", status.FirstResponseStatus);
        Assert.Equal("Met", status.ResolutionStatus);
    }

    [Fact]
    public void GetStatus_ResolvedTicket_IsNeverAtRisk()
    {
        var timeProvider = new TestTimeProvider(_baseTime.AddMinutes(10));
        var service = new SlaService(timeProvider);

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Resolved",
            CreatedAt = _baseTime,
            FirstResponseDueAt = _baseTime.AddMinutes(15),
            FirstRespondedAt = _baseTime.AddMinutes(5),
            ResolutionDueAt = _baseTime.AddHours(4),
            ResolvedAt = _baseTime.AddMinutes(10)
        };

        var status = service.GetStatus(ticket);

        // Milestone 4 requirement: Prevent resolved tickets with completed SLA checks
        // from being incorrectly counted as currently at risk.
        Assert.NotEqual("At Risk", status.FirstResponseStatus);
        Assert.NotEqual("At Risk", status.ResolutionStatus);
        Assert.Equal("Met", status.FirstResponseStatus);
        Assert.Equal("Met", status.ResolutionStatus);
    }

    [Fact]
    public void GetStatus_NullDeadline_ReturnsUnavailable()
    {
        var service = new SlaService();

        var ticket = new Ticket
        {
            Id = 1,
            Status = "Open",
            FirstResponseDueAt = null,
            ResolutionDueAt = null
        };

        var status = service.GetStatus(ticket);

        Assert.Equal("Unavailable", status.FirstResponseStatus);
        Assert.Equal("Unavailable", status.ResolutionStatus);
    }

    [Fact]
    public void GetStatus_ExactDeadlineBoundaryChecks()
    {
        var deadline = _baseTime.AddHours(1);

        // Exactly on deadline -> Met
        var ticketMet = new Ticket
        {
            Id = 1,
            Status = "InProgress",
            FirstResponseDueAt = deadline,
            FirstRespondedAt = deadline,
            ResolutionDueAt = _baseTime.AddHours(8)
        };
        var statusMet = new SlaService(new TestTimeProvider(_baseTime)).GetStatus(ticketMet);
        Assert.Equal("Met", statusMet.FirstResponseStatus);

        // Exactly 1 second late -> Breached
        var ticketLate = new Ticket
        {
            Id = 2,
            Status = "InProgress",
            FirstResponseDueAt = deadline,
            FirstRespondedAt = deadline.AddSeconds(1),
            ResolutionDueAt = _baseTime.AddHours(8)
        };
        var statusLate = new SlaService(new TestTimeProvider(_baseTime)).GetStatus(ticketLate);
        Assert.Equal("Breached", statusLate.FirstResponseStatus);

        // Exactly on deadline but not completed (now == deadline) -> At Risk
        var ticketAtBoundary = new Ticket
        {
            Id = 3,
            Status = "Open",
            FirstResponseDueAt = deadline,
            FirstRespondedAt = null,
            ResolutionDueAt = _baseTime.AddHours(8)
        };
        var statusBoundary = new SlaService(new TestTimeProvider(deadline)).GetStatus(ticketAtBoundary);
        Assert.Equal("At Risk", statusBoundary.FirstResponseStatus);

        // 1 second past deadline and not completed -> Breached
        var statusOverdue = new SlaService(new TestTimeProvider(deadline.AddSeconds(1))).GetStatus(ticketAtBoundary);
        Assert.Equal("Breached", statusOverdue.FirstResponseStatus);
    }
}
