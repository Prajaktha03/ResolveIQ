using ResolveIQ.Api.DTOs;
using ResolveIQ.Api.Models;

namespace ResolveIQ.Api.Services;

public interface ITicketService
{
    Task<List<Ticket>> GetAllAsync();
    Task<Ticket?> GetByIdAsync(int id);
    Task<Ticket> CreateAsync(CreateTicketRequest request, int userId);
    Task<Ticket?> UpdateAsync(
        int id, UpdateTicketRequest request, int userId);
}