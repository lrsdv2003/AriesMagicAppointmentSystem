using System.Security.Claims;
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

        public CommunicationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? id, string filter = "all", string? q = null)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var isInternal = IsInternalUser();
            var memberships = await _context.ConversationParticipants
                .Where(p => p.UserId == userId)
                .Include(p => p.Conversation!).ThenInclude(c => c.Booking!).ThenInclude(b => b.Client)
                .Include(p => p.Conversation!).ThenInclude(c => c.Booking!).ThenInclude(b => b.Service)
                .Include(p => p.Conversation!).ThenInclude(c => c.Participants).ThenInclude(p => p.User)
                .Include(p => p.Conversation!).ThenInclude(c => c.Messages)
                .AsSplitQuery()
                .ToListAsync();

            if (!isInternal)
            {
                memberships = memberships
                    .Where(p => !IsInternalConversation(p.Conversation!))
                    .ToList();
            }

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

            var items = memberships.Select(m => BuildListItem(m, userId, roleMap)).ToList();
            items = ApplyFilter(items, memberships, filter).OrderByDescending(x => x.LastActivityAt).ToList();

            var model = new CommunicationCenterViewModel
            {
                Conversations = items,
                Filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter,
                Search = q?.Trim() ?? string.Empty,
                IsInternalUser = isInternal,
                UnreadConversationCount = items.Count(x => x.UnreadCount > 0)
            };

            if (isInternal)
            {
                model.InternalUsers = await GetInternalUserOptionsAsync(userId);
            }

            var selectedId = id.HasValue && items.Any(x => x.Id == id.Value)
                ? id.Value
                : items.FirstOrDefault()?.Id;

            if (selectedId.HasValue)
            {
                model.SelectedConversation = await BuildConversationDetailAsync(selectedId.Value, userId, roleMap);
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

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartSupport()
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var existingId = await _context.ConversationParticipants
                .Where(p => p.UserId == userId && p.Conversation!.ConversationType == ConversationTypes.ClientSupport)
                .Select(p => p.ConversationId)
                .FirstOrDefaultAsync();

            if (existingId > 0)
                return RedirectToAction(nameof(Index), new { id = existingId });

            var conversation = new Conversation
            {
                ConversationType = ConversationTypes.ClientSupport,
                Title = "Aries Magic Support",
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            var participantIds = await GetInternalUserIdsAsync(includeOwner: false);
            participantIds.Add(userId);
            await AddParticipantsAsync(conversation.Id, participantIds);

            return RedirectToAction(nameof(Index), new { id = conversation.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartBookingConversation(int bookingId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == bookingId);
            if (booking == null) return NotFound();

            if (User.IsInRole("Client") && booking.ApplicationUserId != userId)
                return Forbid();
            if (!User.IsInRole("Client") && !IsInternalUser())
                return Forbid();

            var conversation = await _context.Conversations
                .Include(c => c.Participants)
                .FirstOrDefaultAsync(c => c.BookingId == bookingId && c.ConversationType == ConversationTypes.Booking);

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    ConversationType = ConversationTypes.Booking,
                    BookingId = bookingId,
                    Title = $"Booking #{bookingId}",
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();

                var participantIds = await GetInternalUserIdsAsync(includeOwner: true);
                if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
                    participantIds.Add(booking.ApplicationUserId);
                participantIds.Add(userId);
                await AddParticipantsAsync(conversation.Id, participantIds);
            }
            else if (!conversation.Participants.Any(p => p.UserId == userId))
            {
                _context.ConversationParticipants.Add(new ConversationParticipant
                {
                    ConversationId = conversation.Id,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index), new { id = conversation.Id });
        }

        [HttpPost]
        [Authorize(Roles = "Staff,Admin,Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateInternal(
            string[] participantIds,
            string? title,
            string? initialMessage,
            string? requestType,
            string? actionLink,
            int? relatedBookingId)
        {
            var userId = CurrentUserId();
            if (userId == null) return Challenge();

            var allowedIds = await GetInternalUserIdsAsync(includeOwner: true);
            var targets = participantIds
                .Where(x => allowedIds.Contains(x) && x != userId)
                .Distinct()
                .ToList();

            if (targets.Count == 0)
            {
                TempData["Error"] = "Select at least one team member.";
                return RedirectToAction(nameof(Index));
            }

            var allParticipants = targets.Append(userId).Distinct().ToList();
            var hasRequest = !string.IsNullOrWhiteSpace(requestType);
            var type = hasRequest
                ? ConversationTypes.OperationalRequest
                : allParticipants.Count == 2 ? ConversationTypes.InternalDirect : ConversationTypes.InternalGroup;

            if (type == ConversationTypes.InternalDirect)
            {
                var targetId = targets[0];
                var existing = await _context.Conversations
                    .Where(c => c.ConversationType == ConversationTypes.InternalDirect)
                    .Where(c => c.Participants.Count == 2)
                    .Where(c => c.Participants.Any(p => p.UserId == userId) && c.Participants.Any(p => p.UserId == targetId))
                    .Select(c => c.Id)
                    .FirstOrDefaultAsync();
                if (existing > 0 && string.IsNullOrWhiteSpace(initialMessage))
                    return RedirectToAction(nameof(Index), new { id = existing });
            }

            if ((type == ConversationTypes.InternalGroup || type == ConversationTypes.OperationalRequest) && string.IsNullOrWhiteSpace(title))
                title = hasRequest ? requestType : "Team Conversation";

            if (relatedBookingId.HasValue && !await _context.Bookings.AnyAsync(b => b.Id == relatedBookingId.Value))
                relatedBookingId = null;

            var conversation = new Conversation
            {
                ConversationType = type,
                Title = title?.Trim(),
                BookingId = relatedBookingId,
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();
            await AddParticipantsAsync(conversation.Id, allParticipants);

            if (!string.IsNullOrWhiteSpace(initialMessage))
            {
                var message = new Message
                {
                    ConversationId = conversation.Id,
                    SenderId = userId,
                    MessageContent = initialMessage.Trim()[..Math.Min(initialMessage.Trim().Length, 4000)],
                    SentAt = DateTime.UtcNow,
                    MessageType = hasRequest ? MessageTypes.ActionRequest : MessageTypes.Text,
                    RequestType = hasRequest ? requestType?.Trim() : null,
                    RequestStatus = hasRequest ? MessageRequestStatuses.Open : null,
                    ActionLink = !string.IsNullOrWhiteSpace(actionLink) && Url.IsLocalUrl(actionLink) ? actionLink.Trim() : null
                };
                _context.Messages.Add(message);
                conversation.UpdatedAt = message.SentAt;
                await AddMessageNotificationsAsync(conversation, message, allParticipants.Where(x => x != userId));
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index), new { id = conversation.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendMessage(int conversationId, string messageContent, string? requestType = null, string? actionLink = null)
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

            var canCreateRequest = IsInternalUser() && !string.IsNullOrWhiteSpace(requestType);
            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = userId,
                MessageContent = content,
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
                content = m.MessageContent,
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
        public async Task<IActionResult> UnreadCount()
        {
            var userId = CurrentUserId();
            if (userId == null) return Json(new { count = 0 });

            var count = await _context.ConversationParticipants
                .Where(p => p.UserId == userId)
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
                .Include(c => c.Messages).ThenInclude(m => m.Sender)
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
            return new ConversationDetailViewModel
            {
                Id = conversation.Id,
                Title = title,
                ConversationType = conversation.ConversationType,
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
                    LastLoginAt = p.User?.LastLoginAt
                }).ToList(),
                Messages = conversation.Messages.OrderBy(m => m.SentAt).TakeLast(250).Select(m => new CommunicationMessageViewModel
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    SenderName = m.Sender?.FullName ?? m.Sender?.Email ?? "User",
                    SenderRole = roleMap.GetValueOrDefault(m.SenderId, "User"),
                    Content = m.MessageContent,
                    SentAt = m.SentAt,
                    IsMine = m.SenderId == userId,
                    IsReadByOthers = conversation.Participants.Where(p => p.UserId != m.SenderId).All(p => p.LastReadAt.HasValue && p.LastReadAt.Value >= m.SentAt),
                    MessageType = m.MessageType,
                    RequestType = m.RequestType,
                    RequestStatus = m.RequestStatus,
                    ActionLink = m.ActionLink
                }).ToList(),
                CanSend = !conversation.IsClosed,
                IsInternal = IsInternalConversation(conversation)
            };
        }

        private ConversationListItemViewModel BuildListItem(ConversationParticipant membership, string userId, Dictionary<string, string> roleMap)
        {
            var conversation = membership.Conversation!;
            var latest = conversation.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault();
            var title = BuildConversationTitle(conversation, userId, roleMap);
            var others = conversation.Participants.Where(p => p.UserId != userId).ToList();
            var primaryOther = others.FirstOrDefault(p => roleMap.GetValueOrDefault(p.UserId) == "Client") ?? others.FirstOrDefault();
            var displayName = primaryOther?.User?.FullName ?? primaryOther?.User?.Email ?? title;
            var displayRole = primaryOther == null
                ? "Conversation"
                : roleMap.GetValueOrDefault(primaryOther.UserId, "User");
            var subtitle = conversation.BookingId.HasValue
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
                UnreadCount = conversation.Messages.Count(m => m.SenderId != userId && (!membership.LastReadAt.HasValue || m.SentAt > membership.LastReadAt.Value)),
                Initials = Initials(primaryOther?.User?.FullName ?? displayName),
                HasOpenRequest = conversation.Messages.Any(m => m.MessageType == MessageTypes.ActionRequest && m.RequestStatus == MessageRequestStatuses.Open)
            };
        }

        private static List<ConversationListItemViewModel> ApplyFilter(List<ConversationListItemViewModel> items, List<ConversationParticipant> memberships, string filter)
        {
            filter = (filter ?? "all").ToLowerInvariant();
            return filter switch
            {
                "unread" => items.Where(x => x.UnreadCount > 0).ToList(),
                "client" => items.Where(x => x.ConversationType == ConversationTypes.ClientSupport || x.ConversationType == ConversationTypes.Booking).ToList(),
                "booking" => items.Where(x => x.ConversationType == ConversationTypes.Booking).ToList(),
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
                var client = conversation.Participants.FirstOrDefault(p => roleMap.GetValueOrDefault(p.UserId) == "Client");
                return client?.UserId == userId ? "Aries Magic Support" : $"Support · {client?.User?.FullName ?? "Client"}";
            }
            if (!string.IsNullOrWhiteSpace(conversation.Title)) return conversation.Title;
            var others = conversation.Participants.Where(p => p.UserId != userId).Select(p => p.User?.FullName ?? p.User?.Email ?? "Team Member");
            return string.Join(", ", others);
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
            var roles = includeOwner ? new[] { "Staff", "Admin", "Owner" } : new[] { "Staff", "Admin" };
            var roleIds = await _context.Roles.Where(r => roles.Contains(r.Name!)).Select(r => r.Id).ToListAsync();
            var ids = await _context.UserRoles.Where(ur => roleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().ToListAsync();
            var activeIds = await _context.Users.Where(u => u.IsActive && ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
            return activeIds.ToHashSet();
        }

        private async Task<List<CommunicationUserOptionViewModel>> GetInternalUserOptionsAsync(string currentUserId)
        {
            var ids = await GetInternalUserIdsAsync(includeOwner: true);
            ids.Remove(currentUserId);
            var users = await _context.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).OrderBy(u => u.FullName).ToListAsync();
            var roles = await GetRoleMapAsync(users.Select(u => u.Id).ToList());
            return users.Select(u => new CommunicationUserOptionViewModel
            {
                UserId = u.Id,
                Name = u.FullName,
                Role = roles.GetValueOrDefault(u.Id, "Team")
            }).ToList();
        }

        private async Task<Dictionary<string, string>> GetRoleMapAsync(IReadOnlyCollection<string> userIds)
        {
            if (userIds.Count == 0) return new Dictionary<string, string>();
            var rows = await (from ur in _context.UserRoles
                              join r in _context.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, RoleName = r.Name! })
                .AsNoTracking()
                .ToListAsync();
            return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.RoleName).FirstOrDefault() ?? "User");
        }

        private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private bool IsInternalUser() => User.IsInRole("Staff") || User.IsInRole("Admin") || User.IsInRole("Owner");
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
