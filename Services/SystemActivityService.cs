using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services
{
    public interface ISystemActivityService
    {
        Task LogAsync(
            SystemActivityType type,
            string description,
            string performedByUserId,
            string? performedByUserName = null,
            string? affectedRecordId = null,
            string? affectedRecordType = null,
            object? metadata = null);

        Task<List<SystemActivity>> GetRecentAsync(int count = 50);

        Task<(List<SystemActivity> Items, int TotalCount)> GetPagedAsync(
            int page = 1,
            int pageSize = 20,
            SystemActivityType? type = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? search = null);
    }

    public class SystemActivityService : ISystemActivityService
    {
        private readonly ApplicationDbContext _context;

        public SystemActivityService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            SystemActivityType type,
            string description,
            string performedByUserId,
            string? performedByUserName = null,
            string? affectedRecordId = null,
            string? affectedRecordType = null,
            object? metadata = null)
        {
            var activity = new SystemActivity
            {
                Type = type,
                Description = description,
                PerformedByUserId = performedByUserId,
                PerformedByUserName = performedByUserName,
                AffectedRecordId = affectedRecordId,
                AffectedRecordType = affectedRecordType,
                MetadataJson = metadata != null ? System.Text.Json.JsonSerializer.Serialize(metadata) : null,
                CreatedAt = DateTime.UtcNow
            };

            _context.SystemActivities.Add(activity);
            await _context.SaveChangesAsync();
        }

        public async Task<List<SystemActivity>> GetRecentAsync(int count = 50)
        {
            count = Math.Clamp(count, 1, 200);

            return await _context.SystemActivities
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<(List<SystemActivity> Items, int TotalCount)> GetPagedAsync(
            int page = 1,
            int pageSize = 20,
            SystemActivityType? type = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string? search = null)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var query = _context.SystemActivities.AsNoTracking().AsQueryable();

            if (type.HasValue)
            {
                query = query.Where(a => a.Type == type.Value);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(a => a.CreatedAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                var toDateInclusive = toDate.Value.AddDays(1);
                query = query.Where(a => a.CreatedAt < toDateInclusive);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(a =>
                    a.Description.ToLower().Contains(term) ||
                    (a.PerformedByUserName != null && a.PerformedByUserName.ToLower().Contains(term)) ||
                    (a.AffectedRecordId != null && a.AffectedRecordId.ToLower().Contains(term)) ||
                    (a.AffectedRecordType != null && a.AffectedRecordType.ToLower().Contains(term)));
            }

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }
    }
}