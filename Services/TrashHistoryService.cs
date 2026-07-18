using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services
{
    public interface ITrashHistoryService
    {
        Task<TrashHistoryIndexViewModel> GetTrashHistoryAsync(TrashHistoryFilterViewModel filters);
        Task<TrashHistoryDetailsViewModel?> GetDetailsAsync(int id);
        Task<int> ArchiveFailedBookingsAsync();
    }

    public class TrashHistoryService : ITrashHistoryService
    {
        private readonly ApplicationDbContext _context;

        public TrashHistoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<TrashHistoryIndexViewModel> GetTrashHistoryAsync(TrashHistoryFilterViewModel filters)
        {
            var query = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Where(b => b.Status == BookingStatus.Declined ||
                           b.Status == BookingStatus.Cancelled ||
                           b.Status == BookingStatus.Expired)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filters.Search))
            {
                var term = filters.Search.Trim().ToLower();
                query = query.Where(b =>
                    b.Id.ToString().Contains(term) ||
                    (b.Client != null && b.Client.FullName != null && b.Client.FullName.ToLower().Contains(term)) ||
                    (b.Client != null && b.Client.Email != null && b.Client.Email.ToLower().Contains(term)) ||
                    (b.ContactNumber != null && b.ContactNumber.ToLower().Contains(term)) ||
                    b.PackageName.ToLower().Contains(term) ||
                    b.EventType.ToLower().Contains(term));
            }

            if (filters.Reason.HasValue)
            {
                query = query.Where(b => b.TrashReason == filters.Reason.Value);
            }

            if (!string.IsNullOrWhiteSpace(filters.PaymentStatus) && filters.PaymentStatus != "All")
            {
                if (filters.PaymentStatus == "No Payment")
                {
                    query = query.Where(b => !b.Payments.Any());
                }
                else
                {
                    query = query.Where(b => b.Payments
                        .OrderByDescending(p => p.UploadedAt)
                        .Select(p => p.Status)
                        .FirstOrDefault() == filters.PaymentStatus);
                }
            }

            if (filters.DateFrom.HasValue)
            {
                var from = filters.DateFrom.Value.Date;
                query = query.Where(b => b.CreatedAt >= from);
            }

            if (filters.DateTo.HasValue)
            {
                var to = filters.DateTo.Value.Date.AddDays(1);
                query = query.Where(b => b.CreatedAt < to);
            }

            var totalCount = await query.CountAsync();
            var page = filters.Page < 1 ? 1 : filters.Page;
            var pageSize = filters.PageSize < 1 ? 20 : filters.PageSize;

            var bookings = await query
                .OrderByDescending(b => b.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var rows = bookings.Select(b => new TrashHistoryRowViewModel
            {
                Id = b.Id,
                BookingCode = $"BK-{b.CreatedAt.Year}-{b.Id:D3}",
                ClientName = b.Client?.FullName ?? "N/A",
                ClientPhone = b.ContactNumber,
                EventType = b.EventType,
                PackageName = b.PackageName,
                EventDate = b.EventDate,
                Reason = b.TrashReason ?? TrashReason.InvalidBookingRequest,
                ReasonNotes = b.TrashNotes,
                PaymentStatus = b.Payments.Any()
                    ? b.Payments.OrderByDescending(p => p.UploadedAt).First().Status
                    : "No Payment",
                ArchivedAt = b.ArchivedAt ?? b.CreatedAt
            }).ToList();

            var availableStaff = await _context.Users
                .Where(u => u.IsActive && _context.UserRoles.Any(ur => ur.UserId == u.Id && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Staff")))
                .Select(u => new ApplicationUser { Id = u.Id, FullName = u.FullName })
                .ToListAsync();

            return new TrashHistoryIndexViewModel
            {
                Filters = filters,
                Bookings = rows,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                AvailableStaff = availableStaff,
                AvailablePaymentStatuses = new List<string> { "No Payment", "Pending", "Verified", "Rejected" }
            };
        }

        public async Task<TrashHistoryDetailsViewModel?> GetDetailsAsync(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null)
            {
                return null;
            }

            var trash = new TrashHistoryDetailViewModel
            {
                Id = booking.Id,
                BookingCode = $"BK-{booking.CreatedAt.Year}-{booking.Id:D3}",
                ClientName = booking.Client?.FullName ?? "N/A",
                ClientEmail = booking.Client?.Email ?? string.Empty,
                ClientPhone = booking.ContactNumber,
                EventType = booking.EventType,
                PackageName = booking.PackageName,
                EventDate = booking.EventDate,
                StartTime = booking.StartTime.TimeOfDay,
                EndTime = booking.EndTime.TimeOfDay,
                PartyVenue = booking.PartyVenue,
                PartyTheme = booking.PartyTheme,
                CelebrantName = booking.CelebrantName,
                Age = booking.Age,
                PaxCount = booking.PaxCount,
                ContactPerson = booking.ContactPerson,
                ContactNumber = booking.ContactNumber,
                BasePrice = booking.BasePrice,
                FinalPrice = booking.FinalPrice,
                RequiredDownpayment = booking.RequiredDownpayment,
                Reason = booking.TrashReason ?? TrashReason.InvalidBookingRequest,
                ReasonNotes = booking.TrashNotes,
                PaymentStatus = booking.Payments.Any()
                    ? booking.Payments.OrderByDescending(p => p.UploadedAt).First().Status
                    : "No Payment",
                ArchivedAt = booking.ArchivedAt ?? booking.CreatedAt,
                AssignedStaffName = booking.AssignedStaffName,
                CreatedAt = booking.CreatedAt,
                OriginalBookingId = booking.OriginalBookingId
            };

            return new TrashHistoryDetailsViewModel { Trash = trash };
        }

        public async Task<int> ArchiveFailedBookingsAsync()
        {
            // This would be called by a background service to automatically archive failed bookings
            // For now, bookings are already marked as Declined/Cancelled/Expired in the main workflow
            // This service would just ensure they have TrashReason and ArchivedAt set

            var bookingsToArchive = await _context.Bookings
                .Where(b => (b.Status == BookingStatus.Declined ||
                            b.Status == BookingStatus.Cancelled ||
                            b.Status == BookingStatus.Expired) &&
                           (!b.ArchivedAt.HasValue || b.TrashReason == null))
                .ToListAsync();

            int archivedCount = 0;
            var now = DateTime.UtcNow;

            foreach (var booking in bookingsToArchive)
            {
                // Determine trash reason based on status and other factors
                booking.TrashReason = DetermineTrashReason(booking);
                booking.TrashNotes = GetTrashNotes(booking);
                booking.ArchivedAt = now;
                archivedCount++;
            }

            if (archivedCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return archivedCount;
        }

        private TrashReason DetermineTrashReason(Booking booking)
        {
            return booking.Status switch
            {
                BookingStatus.Declined => TrashReason.RejectedByAdmin,
                BookingStatus.Cancelled => TrashReason.ClientAbandonedBooking,
                BookingStatus.Expired => TrashReason.BookingRequestExpired,
                _ => TrashReason.InvalidBookingRequest
            };
        }

        private string GetTrashNotes(Booking booking)
        {
            return booking.Status switch
            {
                BookingStatus.Declined => "Booking was declined by staff/admin.",
                BookingStatus.Cancelled => "Booking was cancelled by the client.",
                BookingStatus.Expired => "Booking request expired without action.",
                _ => "Booking request was invalid or abandoned."
            };
        }
    }
}