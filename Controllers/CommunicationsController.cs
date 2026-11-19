using System.Security.Claims;
using System.IO;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Extensions;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize]
    public class CommunicationsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment? _environment;

        // Optional parameter preserves existing controller-only isolated integration tests.
        public CommunicationsController(ApplicationDbContext context, IWebHostEnvironment? environment = null)
        {
            _context = context;
            _environment = environment;
        }

        // The original Staff photo is protected in App_Data, not a public URL.
        private static string? SenderAvatarUrl(int conversationId, string userId, string? stored)
        {
            if (conversationId <= 0 || string.IsNullOrWhiteSpace(userId) ||
                (stored?.StartsWith("/private-profiles/", StringComparison.Ordinal) != true &&
                 stored?.StartsWith("/uploads/profiles/", StringComparison.Ordinal) != true))
                return null;
            return "/Communications/ParticipantAvatar?conversationId=" + conversationId
                   + "&userId=" + Uri.EscapeDataString(userId);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ParticipantAvatar(int conversationId, string? userId)
        {
            var viewer = CurrentUserId();
            if (viewer == null) return Challenge();
            if (conversationId <= 0 || string.IsNullOrWhiteSpace(userId)) return NotFound();
            if (!await CanAccessConversationAsync(conversationId, viewer)) return Forbid();
            if (!await _context.ConversationParticipants.AsNoTracking().AnyAsync(
                    p => p.ConversationId == conversationId && p.UserId == userId))
                return NotFound();
            var stored = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.ProfilePicturePath).FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(stored) || _environment == null) return NotFound();
            const string privatePrefix = "/private-profiles/", publicPrefix = "/uploads/profiles/";
            var privatePhoto = stored.StartsWith(privatePrefix, StringComparison.Ordinal);
            if (!privatePhoto && !stored.StartsWith(publicPrefix, StringComparison.Ordinal)) return NotFound();
            var fileName = stored[(privatePhoto ? privatePrefix.Length : publicPrefix.Length)..];
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (fileName != Path.GetFileName(fileName) || fileName.Contains('\\')
                || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(fileName), "N", out _)
                || !new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(extension))
                return NotFound();
            var folder = privatePhoto
                ? Path.Combine(_environment.ContentRootPath, "App_Data", "profiles")
                : Path.Combine(_environment.WebRootPath, "uploads", "profiles");
            var path = Path.Combine(folder, fileName);
            if (!System.IO.File.Exists(path)) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return PhysicalFile(path, extension switch { ".png"=>"image/png", ".webp"=>"image/webp", _=>"image/jpeg" });
        }

        public async Task<IActionResult> Index(int? id, string filter = "all", string? q = null, string? view = null, int? relatedBookingId = null)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var (items, roleMap) = await LoadVisibleConversationsAsync(userId, filter, q);
            var isInternal = IsInternalUser();

            var model = new CommunicationCenterViewModel
            {
                Conversations = items,
                Filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter,
                Search = q?.Trim() ?? string.Empty,
                IsInternalUser = isInternal,
                UnreadConversationCount = items.Count(x => x.UnreadCount > 0)
            };

            var selectedId = view == "inbox"
                ? (int?)null : id.HasValue && items.Any(x => x.Id == id.Value)
                ? id.Value
                : items.FirstOrDefault()?.Id;

            if (selectedId.HasValue)
            {
                model.SelectedConversation = await BuildConversationDetailAsync(selectedId.Value, userId, roleMap);
                if (model.SelectedConversation != null &&
                    relatedBookingId.HasValue &&
                    model.SelectedConversation.BookingOptions.Any(b => b.Id == relatedBookingId.Value))
                    model.SelectedConversation.SelectedRelatedBookingId = relatedBookingId;
                var membership = await _context.ConversationParticipants
                    .FirstOrDefaultAsync(p => p.ConversationId == selectedId.Value && p.UserId == userId);
                if (membership != null)
                {
                    membership.LastReadAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            return View(model);
        }

        // The sidebar refresh reuses the same authorization, search and order rules
        // as Index. It never selects/marks-read a thread or creates participants.
        private async Task<(List<ConversationListItemViewModel> Items,
            Dictionary<string, string> RoleMap)> LoadVisibleConversationsAsync(
            string userId, string filter, string? q)
        {
            var isInternal = IsInternalUser();
            // Search retains full message matching; ordinary inbox loads only each latest preview.
            var previewLimit = string.IsNullOrWhiteSpace(q) ? 1 : int.MaxValue;
            var memberships = await _context.ConversationParticipants
                .AsNoTrackingWithIdentityResolution()
                .Where(p => p.UserId == userId)
                .Include(p => p.Conversation!).ThenInclude(c => c.Booking!).ThenInclude(b => b.Client)
                .Include(p => p.Conversation!).ThenInclude(c => c.Booking!).ThenInclude(b => b.Service)
                .Include(p => p.Conversation!).ThenInclude(c => c.Participants).ThenInclude(p => p.User)
                .Include(p => p.Conversation!).ThenInclude(c => c.Messages.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(previewLimit))
                .AsSplitQuery()
                .ToListAsync();

            // Only canonical Client support threads appear in either inbox.
            // Legacy per-booking test conversations remain in storage for compatibility/audit.
            memberships = memberships.Where(p =>
                p.Conversation!.ConversationType != ConversationTypes.Booking &&
                (p.Conversation.ConversationType != ConversationTypes.ClientSupport ||
                 p.Conversation.SupportClientId != null) &&
                (isInternal || !IsInternalConversation(p.Conversation))).ToList();

            var allUserIds = memberships
                .SelectMany(p => p.Conversation!.Participants)
                .Select(p => p.UserId)
                .Append(userId)
                .Distinct()
                .ToList();
            var roleMap = await GetRoleMapAsync(allUserIds);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                memberships = memberships.Where(p => ConversationMatches(p.Conversation!, term)).ToList();
            }

            var counts = await _context.ConversationParticipants.Where(p => p.UserId == userId)
                .Select(p => new {
                    p.ConversationId,
                    Unread = p.Conversation!.Messages.Count(m => m.SenderId != userId && (!p.LastReadAt.HasValue || m.SentAt > p.LastReadAt.Value)),
                    Open = p.Conversation!.Messages.Any(m => m.MessageType == MessageTypes.ActionRequest && m.RequestStatus == MessageRequestStatuses.Open)
                }).ToDictionaryAsync(p => p.ConversationId, HttpContext.RequestAborted);
            var items = memberships.Select(m => BuildListItem(m, userId, roleMap, counts[m.ConversationId].Unread, counts[m.ConversationId].Open)).ToList();
            items = ApplyFilter(items, filter).OrderByDescending(x => x.LastActivityAt).ToList();
            return (items, roleMap);
        }

        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ConversationSummaries(string filter = "all", string? q = null)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();
            var (items, _) = await LoadVisibleConversationsAsync(userId, filter, q);
            return Json(new
            {
                items = items.Select(x => new
                {
                    id = x.Id,
                    displayName = x.DisplayName,
                    displayRole = x.DisplayRole,
                    subtitle = x.Subtitle,
                    avatarUrl = x.AvatarUrl,
                    initials = x.Initials,
                    latestMessage = x.LatestMessage,
                    lastActivityAt = x.LastActivityAt,
                    unreadCount = x.UnreadCount
                }),
                unreadConversationCount = items.Count(x => x.UnreadCount > 0)
            });
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSupport()
        {
            var clientId = CurrentUserId();
            if (clientId == null) return Challenge();
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var conversation = await GetOrCreateClientSupportAsync(clientId, includeCurrentInternal: false);
            await transaction.CommitAsync();
            return RedirectToAction(nameof(Index), new { id = conversation.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartBookingConversation(int bookingId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();
            var booking = await _context.Bookings.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null) return NotFound();

            if (User.IsInRole("Client") && booking.ApplicationUserId != userId) return Forbid();
            if (!User.IsInRole("Client") && !IsInternalUser()) return Forbid();
            if (string.IsNullOrWhiteSpace(booking.ApplicationUserId)) return NotFound();

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var conversation = await GetOrCreateClientSupportAsync(
                booking.ApplicationUserId, includeCurrentInternal: IsInternalUser());
            await transaction.CommitAsync();
            // The booking is context for the next message, never a separate conversation.
            return RedirectToAction(nameof(Index), new { id = conversation.Id, relatedBookingId = booking.Id });
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestBookingCancellation(int bookingId, string? reason)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8 || reason.Length > 1000)
            {
                TempData["Error"] = "Explain why you need to cancel in at least 8 characters.";
                return RedirectToAction("Details", "Bookings", new { id = bookingId });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var booking = await _context.Bookings.Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.ApplicationUserId == userId);
            if (booking == null) return NotFound();

            if (booking.CancellationRequestMessageId.HasValue)
            {
                var existingConversation = await GetOrCreateClientSupportAsync(userId, includeCurrentInternal: false);
                await transaction.CommitAsync();
                return RedirectToAction(nameof(Index), new { id = existingConversation.Id, relatedBookingId = bookingId });
            }

            var initialPayment = booking.Payments.Where(p => p.Status == PaymentStatus.Verified && p.InitialVerificationAtUtc.HasValue)
                .OrderBy(p => p.InitialVerificationAtUtc).FirstOrDefault();
            if (initialPayment == null || DateTime.UtcNow < initialPayment.InitialVerificationAtUtc!.Value.AddHours(24)
                || booking.Status is not (BookingStatus.Pending or BookingStatus.AwaitingDownpayment or BookingStatus.AwaitingVerification or BookingStatus.Confirmed)
                || booking.RefundEligible
                || booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired))
            {
                TempData["Error"] = "This booking is not eligible for a cancellation review request. Contact Staff through the booking conversation if you need general assistance.";
                return RedirectToAction("Details", "Bookings", new { id = bookingId });
            }

            var conversation = await GetOrCreateClientSupportAsync(userId, includeCurrentInternal: false);
            var participantIds = conversation.Participants.Select(p => p.UserId).Distinct().ToList();

            var message = new Message
            {
                ConversationId = conversation.Id,
                SenderId = userId,
                MessageContent = $"Client cancellation request · BK-{booking.CreatedAt.Year}-{booking.Id:D3}: {reason}",
                RelatedBookingId = booking.Id,
                SentAt = DateTime.UtcNow,
                MessageType = MessageTypes.Text
            };
            _context.Messages.Add(message);
            conversation.UpdatedAt = message.SentAt;
            await _context.SaveChangesAsync();

            booking.CancellationRequestAtUtc = message.SentAt;
            booking.CancellationRequestByUserId = userId;
            booking.CancellationRequestReason = reason;
            booking.CancellationRequestMessageId = message.Id;
            _context.SystemActivities.Add(new SystemActivity
            {
                Type = SystemActivityType.BookingCancellationRequested,
                Description = $"Booking #{booking.Id} cancellation review requested by Client.",
                PerformedByUserId = userId,
                PerformedByUserName = User.Identity?.Name,
                AffectedRecordId = booking.Id.ToString(),
                AffectedRecordType = "Booking",
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { BookingId = booking.Id, MessageId = message.Id, AtUtc = message.SentAt })
            });
            await AddMessageNotificationsAsync(conversation, message, participantIds.Where(id => id != userId));
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = "Your cancellation request was sent to Staff through your private support conversation. Staff will review it; no refund has been approved or paid.";
            return RedirectToAction(nameof(Index), new { id = conversation.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int conversationId, string messageContent, string? requestType = null, string? actionLink = null, int? relatedBookingId = null)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();
            if (!await CanAccessConversationAsync(conversationId, userId)) return Forbid();

            var content = messageContent?.Trim();
            if (string.IsNullOrWhiteSpace(content))
                return BadRequest(new { message = "Message cannot be empty." });
            if (content.Length > 4000) content = content[..4000];

            var conversation = await _context.Conversations
                .Include(c => c.Participants)
                .FirstAsync(c => c.Id == conversationId);
            if (User.IsInRole("Client") && IsInternalConversation(conversation)) return Forbid();
            if (conversation.IsClosed) return BadRequest(new { message = "This conversation is closed." });
            // Never trust a client-submitted booking ID without validating the canonical owner.
            if (relatedBookingId.HasValue)
            {
                if (conversation.ConversationType != ConversationTypes.ClientSupport ||
                    string.IsNullOrWhiteSpace(conversation.SupportClientId) ||
                    !await _context.Bookings.AnyAsync(b => b.Id == relatedBookingId &&
                         b.ApplicationUserId == conversation.SupportClientId))
                    return BadRequest(new { message = "Select a booking belonging to this conversation." });
            }

            var canCreateRequest = IsInternalUser() && !string.IsNullOrWhiteSpace(requestType);
            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = userId,
                MessageContent = content,
                RelatedBookingId = relatedBookingId,
                SentAt = DateTime.UtcNow,
                MessageType = canCreateRequest ? MessageTypes.ActionRequest : MessageTypes.Text,
                RequestType = canCreateRequest ? requestType?.Trim() : null,
                RequestStatus = canCreateRequest ? MessageRequestStatuses.Open : null,
                ActionLink = canCreateRequest && !string.IsNullOrWhiteSpace(actionLink) && Url.IsLocalUrl(actionLink)
                    ? actionLink.Trim()
                    : null
            };
            _context.Messages.Add(message);
            conversation.UpdatedAt = message.SentAt;

            var senderMembership = conversation.Participants.FirstOrDefault(p => p.UserId == userId);
            if (senderMembership != null) senderMembership.LastReadAt = message.SentAt;

            await AddMessageNotificationsAsync(conversation, message, conversation.Participants.Where(p => p.UserId != userId).Select(p => p.UserId));
            await _context.SaveChangesAsync();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return Json(new { ok = true, messageId = message.Id });

            return RedirectToAction(nameof(Index), new { id = conversationId });
        }

        [HttpGet]
        public async Task<IActionResult> MessagesSince(int id, int afterId = 0)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();
            if (!await CanAccessConversationAsync(id, userId)) return Forbid();

            var conversation = await _context.Conversations
                .AsNoTracking()
                .Include(c => c.Participants).ThenInclude(p => p.User)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            if (User.IsInRole("Client") && IsInternalConversation(conversation)) return Forbid();

            var messages = await _context.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == id && m.Id > afterId)
                .Include(m => m.Sender)
                .OrderBy(m => m.Id)
                .Take(100)
                .ToListAsync();

            var roleMap = await GetRoleMapAsync(messages.Select(m => m.SenderId).Distinct().ToList());
            var now = DateTime.UtcNow;
            var membership = await _context.ConversationParticipants.FirstOrDefaultAsync(p => p.ConversationId == id && p.UserId == userId);
            if (membership != null)
            {
                membership.LastReadAt = now;
                await _context.SaveChangesAsync();
            }

            var payload = messages.Select(m => new
            {
                id = m.Id,
                senderId = m.SenderId,
                senderName = m.Sender?.FullName ?? m.Sender?.Email ?? "User",
                senderRole = roleMap.GetValueOrDefault(m.SenderId, "User"),
                senderAvatarUrl = SenderAvatarUrl(id, m.SenderId, m.Sender?.ProfilePicturePath),
                content = m.MessageContent,
                relatedBookingId = m.RelatedBookingId,
                bookingUrl = m.RelatedBookingId.HasValue && (User.IsInRole("Client") || User.IsInRole("Staff") || User.IsInRole("Owner"))
                    ? Url.Action("Details", "Bookings", new { id = m.RelatedBookingId.Value }) : null,
                sentAt = m.SentAt,
                isMine = m.SenderId == userId,
                messageType = m.MessageType,
                requestType = m.RequestType,
                requestStatus = m.RequestStatus,
                actionLink = User.CanFollowModuleLink(m.ActionLink) ? m.ActionLink : null
            });

            return Json(payload);
        }

        [HttpGet]
        public async Task<IActionResult> ReadStates(int id)
        {
            var userId = CurrentUserId();
            if (userId == null) return Unauthorized();
            if (!await CanAccessConversationAsync(id, userId)) return Forbid();
            var conversation = await _context.Conversations.AsNoTracking()
                .Include(c => c.Participants)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (conversation == null) return NotFound();
            var messages = await _context.Messages.AsNoTracking()
                .Where(m => m.ConversationId == id && m.SenderId == userId)
                .OrderByDescending(m => m.Id).Take(100)
                .Select(m => new { m.Id, m.SenderId, m.SentAt }).ToListAsync();
            return Json(messages.Select(m => new {
                id = m.Id,
                seen = HasRecipientRead(conversation, m.SenderId, m.SentAt)
            }));
        }

        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var userId = CurrentUserId();
            if (userId == null) return Json(new { count = 0 });

            var count = await _context.ConversationParticipants
                .Where(p => p.UserId == userId &&
                    p.Conversation!.ConversationType != ConversationTypes.Booking &&
                    (p.Conversation.ConversationType != ConversationTypes.ClientSupport ||
                     p.Conversation.SupportClientId != null))
                .CountAsync(p => p.Conversation!.Messages.Any(m =>
                    m.SenderId != userId && (!p.LastReadAt.HasValue || m.SentAt > p.LastReadAt.Value)));
            return Json(new { count });
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteRequest(int messageId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var message = await _context.Messages
                .Include(m => m.Conversation)!.ThenInclude(c => c!.Participants)
                .FirstOrDefaultAsync(m => m.Id == messageId && m.MessageType == MessageTypes.ActionRequest);
            if (message == null) return NotFound();
            if (!await CanAccessConversationAsync(message.ConversationId, userId)) return Forbid();

            message.RequestStatus = MessageRequestStatuses.Completed;
            message.Conversation!.UpdatedAt = DateTime.UtcNow;
            if (message.SenderId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = message.SenderId,
                    Title = "Request Completed",
                    Message = $"Your {message.RequestType ?? "operational"} request was marked completed.",
                    Link = $"/Communications/Index/{message.ConversationId}",
                    CreatedAt = DateTime.Now,
                    IsRead = false
                });
            }
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index), new { id = message.ConversationId });
        }

        private async Task<ConversationDetailViewModel?> BuildConversationDetailAsync(int conversationId, string userId, Dictionary<string, string> roleMap)
        {
            var conversation = await _context.Conversations
                .AsNoTracking()
                .Include(c => c.Booking)!.ThenInclude(b => b!.Client)
                .Include(c => c.Booking)!.ThenInclude(b => b!.Service)
                .Include(c => c.Participants).ThenInclude(p => p.User)
                .Include(c => c.Messages.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).Take(250)).ThenInclude(m => m.Sender)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Id == conversationId);
            if (conversation == null) return null;

            var missingRoleIds = conversation.Participants.Select(p => p.UserId).Where(id => !roleMap.ContainsKey(id)).ToList();
            if (missingRoleIds.Count > 0)
            {
                var extraRoles = await GetRoleMapAsync(missingRoleIds);
                foreach (var pair in extraRoles) roleMap[pair.Key] = pair.Value;
            }

            var title = BuildConversationTitle(conversation, userId, roleMap);
            var bookingOptions = string.IsNullOrWhiteSpace(conversation.SupportClientId)
                ? new List<ClientBookingReferenceViewModel>()
                : await _context.Bookings.AsNoTracking()
                    .Where(b => b.ApplicationUserId == conversation.SupportClientId)
                    .OrderByDescending(b => b.EventDate).ThenByDescending(b => b.Id)
                    .Select(b => new ClientBookingReferenceViewModel {
                        Id = b.Id, Label = "Booking #" + b.Id + " · " + b.PackageName
                    }).Take(100).ToListAsync();
            return new ConversationDetailViewModel
            {
                Id = conversation.Id,
                Title = title,
                ConversationType = conversation.ConversationType,
                SupportClientId = conversation.SupportClientId,
                BookingOptions = bookingOptions,
                ParticipantSummary = string.Join(" · ", conversation.Participants
                    .Where(p => p.UserId != userId)
                    .Select(p => $"{p.User?.FullName ?? p.User?.Email ?? "User"} ({roleMap.GetValueOrDefault(p.UserId, "User")})")
                    .Take(4)),
                BookingId = conversation.BookingId,
                Booking = conversation.Booking == null ? null : new BookingConversationContextViewModel
                {
                    Id = conversation.Booking.Id,
                    ClientName = conversation.Booking.Client?.FullName ?? "Client",
                    ServiceName = conversation.Booking.Service?.Name ?? "Service",
                    PackageName = conversation.Booking.PackageName,
                    EventDate = conversation.Booking.EventDate,
                    StartTime = conversation.Booking.StartTime,
                    Status = conversation.Booking.Status
                },
                Participants = conversation.Participants.Select(p => new CommunicationParticipantViewModel
                {
                    UserId = p.UserId,
                    Name = p.User?.FullName ?? p.User?.Email ?? "User",
                    Role = roleMap.GetValueOrDefault(p.UserId, "User"),
                    Initials = Initials(p.User?.FullName ?? p.User?.Email ?? "User"),
                    AvatarUrl = SenderAvatarUrl(conversation.Id, p.UserId, p.User?.ProfilePicturePath),
                    LastLoginAt = p.User?.LastLoginAt
                }).ToList(),
                Messages = conversation.Messages.OrderBy(m => m.SentAt).ThenBy(m => m.Id).Select(m => new CommunicationMessageViewModel
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    SenderName = m.Sender?.FullName ?? m.Sender?.Email ?? "User",
                    SenderRole = roleMap.GetValueOrDefault(m.SenderId, "User"),
                    SenderAvatarUrl = SenderAvatarUrl(conversation.Id, m.SenderId, m.Sender?.ProfilePicturePath),
                    Content = m.MessageContent,
                    RelatedBookingId = m.RelatedBookingId,
                    SentAt = m.SentAt,
                    IsMine = m.SenderId == userId,
                    IsReadByOthers = HasRecipientRead(conversation, m.SenderId, m.SentAt),
                    MessageType = m.MessageType,
                    RequestType = m.RequestType,
                    RequestStatus = m.RequestStatus,
                    ActionLink = m.ActionLink
                }).ToList(),
                CanSend = !conversation.IsClosed,
                IsInternal = IsInternalConversation(conversation)
            };
        }

        private ConversationListItemViewModel BuildListItem(ConversationParticipant membership, string userId, Dictionary<string, string> roleMap, int unreadCount, bool hasOpenRequest)
        {
            var conversation = membership.Conversation!;
            var latest = conversation.Messages.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).FirstOrDefault();
            var title = BuildConversationTitle(conversation, userId, roleMap);
            var others = conversation.Participants.Where(p => p.UserId != userId).ToList();
            var primaryOther = User.IsInRole("Client")
                ? others.FirstOrDefault(p => roleMap.GetValueOrDefault(p.UserId) == "Staff") ?? others.FirstOrDefault()
                : others.FirstOrDefault(p => roleMap.GetValueOrDefault(p.UserId) == "Client") ?? others.FirstOrDefault();
            var displayName = primaryOther?.User?.FullName ?? primaryOther?.User?.Email ?? title;
            var displayRole = primaryOther == null
                ? "Conversation"
                : roleMap.GetValueOrDefault(primaryOther.UserId, "User");
            var subtitle = conversation.ConversationType == ConversationTypes.ClientSupport
                ? $"{displayRole} · One chat for all bookings"
                : conversation.BookingId.HasValue
                    ? $"{displayRole} · Booking #{conversation.BookingId}"
                    : displayRole;
            var preview = latest?.MessageContent ?? "No messages yet";
            if (preview.Length > 74) preview = preview[..74] + "…";

            return new ConversationListItemViewModel
            {
                Id = conversation.Id,
                Title = title,
                Subtitle = subtitle,
                DisplayName = displayName,
                DisplayRole = displayRole,
                ConversationType = conversation.ConversationType,
                BookingId = conversation.BookingId,
                LatestMessage = preview,
                LastActivityAt = latest?.SentAt ?? conversation.UpdatedAt,
                UnreadCount = unreadCount,
                Initials = Initials(primaryOther?.User?.FullName ?? displayName),
                AvatarUrl = primaryOther == null ? null : SenderAvatarUrl(conversation.Id, primaryOther.UserId, primaryOther.User?.ProfilePicturePath),
                HasOpenRequest = hasOpenRequest
            };
        }

        private static List<ConversationListItemViewModel> ApplyFilter(List<ConversationListItemViewModel> items, string filter)
        {
            filter = (filter ?? "all").ToLowerInvariant();
            return filter switch
            {
                "unread" => items.Where(x => x.UnreadCount > 0).ToList(),
                "client" => items.Where(x => x.ConversationType == ConversationTypes.ClientSupport || x.ConversationType == ConversationTypes.Booking).ToList(),
                "booking" => items.Where(x => x.ConversationType == ConversationTypes.ClientSupport).ToList(),
                "internal" => items.Where(x => x.ConversationType == ConversationTypes.InternalDirect || x.ConversationType == ConversationTypes.InternalGroup).ToList(),
                "requests" => items.Where(x => x.ConversationType == ConversationTypes.OperationalRequest || x.HasOpenRequest).ToList(),
                "support" => items.Where(x => x.ConversationType == ConversationTypes.ClientSupport).ToList(),
                _ => items
            };
        }

        private static bool ConversationMatches(Conversation conversation, string term)
        {
            var comparison = StringComparison.OrdinalIgnoreCase;
            return (conversation.Title?.Contains(term, comparison) ?? false)
                || conversation.Id.ToString().Contains(term, comparison)
                || (conversation.BookingId?.ToString().Contains(term, comparison) ?? false)
                || (conversation.Booking?.Client?.FullName?.Contains(term, comparison) ?? false)
                || (conversation.Booking?.PackageName?.Contains(term, comparison) ?? false)
                || conversation.Participants.Any(p => (p.User?.FullName?.Contains(term, comparison) ?? false) || (p.User?.Email?.Contains(term, comparison) ?? false))
                || conversation.Messages.Any(m => m.MessageContent.Contains(term, comparison));
        }

        private static string BuildConversationTitle(Conversation conversation, string userId, Dictionary<string, string> roleMap)
        {
            if (conversation.ConversationType == ConversationTypes.Booking)
                return $"Booking #{conversation.BookingId} · {conversation.Booking?.PackageName ?? conversation.Booking?.Service?.Name ?? "Event"}";
            if (conversation.ConversationType == ConversationTypes.ClientSupport)
            {
                var client = conversation.Participants.FirstOrDefault(p => p.UserId == conversation.SupportClientId)
                    ?? conversation.Participants.FirstOrDefault(p => roleMap.GetValueOrDefault(p.UserId) == "Client");
                return client?.UserId == userId ? "Aries Magic Support" : $"Support · {client?.User?.FullName ?? "Client"}";
            }
            if (!string.IsNullOrWhiteSpace(conversation.Title)) return conversation.Title;
            var others = conversation.Participants.Where(p => p.UserId != userId).Select(p => p.User?.FullName ?? p.User?.Email ?? "Team Member");
            return string.Join(", ", others);
        }

        private async Task<Conversation> GetOrCreateClientSupportAsync(string clientId, bool includeCurrentInternal)
        {
            var conversation = await _context.Conversations.Include(c => c.Participants)
                .FirstOrDefaultAsync(c => c.ConversationType == ConversationTypes.ClientSupport &&
                    c.SupportClientId == clientId);
            if (conversation == null)
            {
                conversation = new Conversation {
                    ConversationType = ConversationTypes.ClientSupport,
                    SupportClientId = clientId,
                    Title = "Aries Magic Support",
                    CreatedByUserId = clientId,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }

            var participants = await GetInternalUserIdsAsync(includeOwner: false);
            participants.Add(clientId);
            if (includeCurrentInternal && IsInternalUser() && CurrentUserId() is string caller)
                participants.Add(caller);
            await AddParticipantsAsync(conversation.Id, participants);
            await _context.Entry(conversation).Collection(c => c.Participants).LoadAsync();
            return conversation;
        }

        // Seen is grounded in an actual recipient read timestamp.
        // Client messages need a support-team read, not every internal member's read.
        private static bool HasRecipientRead(Conversation conversation, string senderId, DateTime sentAt)
        {
            if (conversation.ConversationType == ConversationTypes.ClientSupport &&
                !string.IsNullOrWhiteSpace(conversation.SupportClientId))
            {
                if (senderId == conversation.SupportClientId)
                    return conversation.Participants.Any(p => p.UserId != senderId &&
                        p.LastReadAt.HasValue && p.LastReadAt.Value >= sentAt);
                var client = conversation.Participants.FirstOrDefault(p => p.UserId == conversation.SupportClientId);
                return client?.LastReadAt >= sentAt;
            }
            return conversation.Participants.Where(p => p.UserId != senderId)
                .All(p => p.LastReadAt.HasValue && p.LastReadAt.Value >= sentAt);
        }

        private async Task AddParticipantsAsync(int conversationId, IEnumerable<string> userIds)
        {
            var ids = userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
            var existing = await _context.ConversationParticipants.Where(p => p.ConversationId == conversationId).Select(p => p.UserId).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var id in ids.Except(existing))
            {
                _context.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversationId,
                    UserId = id,
                    JoinedAt = now
                });
            }
            await _context.SaveChangesAsync();
        }

        private async Task AddMessageNotificationsAsync(Conversation conversation, Message message, IEnumerable<string> recipientIds)
        {
            var title = message.MessageType == MessageTypes.ActionRequest
                ? "New Request"
                : conversation.ConversationType is ConversationTypes.InternalDirect or ConversationTypes.InternalGroup or ConversationTypes.OperationalRequest
                    ? "New Internal Message"
                    : "New Message";
            var preview = message.MessageContent.Length > 120 ? message.MessageContent[..120] + "…" : message.MessageContent;
            foreach (var recipientId in recipientIds.Distinct())
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipientId,
                    Title = title,
                    Message = preview,
                    Link = $"/Communications/Index/{conversation.Id}",
                    CreatedAt = DateTime.Now,
                    IsRead = false
                });
            }
            await Task.CompletedTask;
        }

        private async Task<bool> CanAccessConversationAsync(int conversationId, string userId)
        {
            return await _context.ConversationParticipants.AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId);
        }

        private async Task<HashSet<string>> GetInternalUserIdsAsync(bool includeOwner)
        {
            var roles = includeOwner ? new[] { "Staff", "Owner" } : new[] { "Staff" };
            var roleIds = await _context.Roles.Where(r => roles.Contains(r.Name!)).Select(r => r.Id).ToListAsync();
            var ids = await _context.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
            var activeIds = await _context.Users.Where(u => u.IsActive && ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
            return activeIds.ToHashSet();
        }

        private async Task<Dictionary<string, string>> GetRoleMapAsync(IReadOnlyCollection<string> userIds)
        {
            if (userIds.Count == 0) return new Dictionary<string, string>();
            var rows = await (from ur in _context.UserRoles
                              join r in _context.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId) && ApplicationRoles.All.Contains(r.Name!)
                              select new { ur.UserId, RoleName = r.Name! })
                .AsNoTracking()
                .ToListAsync();
            return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).FirstOrDefault() ?? "User");
        }

        private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private bool IsInternalUser() => User.IsInRole("Staff") || User.IsInRole("Owner");
        private static bool IsInternalConversation(Conversation conversation) =>
            conversation.ConversationType is ConversationTypes.InternalDirect or ConversationTypes.InternalGroup or ConversationTypes.OperationalRequest;

        private static string Initials(string value)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "AM";
            return string.Concat(parts.Take(2).Select(x => char.ToUpperInvariant(x[0])));
        }
    }
}
