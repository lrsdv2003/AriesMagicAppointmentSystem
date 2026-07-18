using System.Text;
using AriesMagicAppointmentSystem.Extensions;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AriesMagicAppointmentSystem.Controllers
{
    /// <summary>
    /// Permanent archive of completed events. Owner and Staff can browse and search
    /// the same records; only Owner can export or print reports. Nobody can edit a
    /// historical record through this controller - there is intentionally no Edit/Delete action.
    /// </summary>
    [Authorize(Roles = "Owner,Staff")]
    public class HistoryController : Controller
    {
        private readonly IHistoryService _historyService;

        public HistoryController(IHistoryService historyService)
        {
            _historyService = historyService;
        }

        public async Task<IActionResult> Index(HistoryFilterViewModel filters)
        {
            var model = await _historyService.GetHistoryAsync(filters);
            return View(model);
        }

        public async Task<IActionResult> Details(int id)
        {
            var model = await _historyService.GetDetailsAsync(id);

            if (model == null)
            {
                return NotFound();
            }

            return View(model);
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Print(HistoryFilterViewModel filters)
        {
            if (!User.CanExportHistoryReports())
            {
                return Forbid();
            }

            var rows = await _historyService.GetHistoryForExportAsync(filters);
            ViewBag.GeneratedAt = DateTime.Now;
            return View(rows);
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> ExportCsv(HistoryFilterViewModel filters)
        {
            if (!User.CanExportHistoryReports())
            {
                return Forbid();
            }

            var rows = await _historyService.GetHistoryForExportAsync(filters);

            var csv = new StringBuilder();
            csv.AppendLine("Booking ID,Client Name,Event Type,Package,Venue,Event Date,Start Time,End Time,Guests,Amount Paid,Remaining Balance,Payment Status,Refund Status,Completed Date");

            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",",
                    Escape(row.BookingCode),
                    Escape(row.ClientName),
                    Escape(row.EventType),
                    Escape(row.PackageName),
                    Escape(row.Venue),
                    row.EventDate.ToString("yyyy-MM-dd"),
                    row.StartTime.ToString("hh:mm tt"),
                    row.EndTime.ToString("hh:mm tt"),
                    row.Guests,
                    row.FinalPrice.ToString("F2"),
                    row.RemainingBalance.ToString("F2"),
                    Escape(row.PaymentStatus),
                    Escape(row.RefundStatus),
                    row.CompletedAt?.ToString("yyyy-MM-dd") ?? ""));
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());

            // Saved with a .csv extension so it opens cleanly in Excel, Numbers, Sheets, etc.
            // (This project does not currently reference a binary .xlsx library.)
            return File(bytes, "text/csv", $"booking-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> ExportPdf(HistoryFilterViewModel filters)
        {
            if (!User.CanExportHistoryReports())
            {
                return Forbid();
            }

            var rows = await _historyService.GetHistoryForExportAsync(filters);
            var pdfBytes = BuildHistoryReportPdf(rows);

            return File(pdfBytes, "application/pdf", $"booking-history-{DateTime.Now:yyyyMMdd-HHmmss}.pdf");
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static byte[] BuildHistoryReportPdf(List<HistoryRowViewModel> rows)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var totalRevenue = rows.Sum(r => r.AmountPaid);

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Aries Magic - Booking History Report").Bold().FontSize(16);
                        col.Item().Text($"Generated {DateTime.Now:MMMM dd, yyyy hh:mm tt}").FontSize(9);
                        col.Item().Text($"Records: {rows.Count}   |   Total Revenue Collected: ₱{totalRevenue:N2}").FontSize(9);
                    });

                    page.Content().PaddingTop(15).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.2f); // Booking
                            columns.RelativeColumn(1.6f); // Client
                            columns.RelativeColumn(1.2f); // Event Type
                            columns.RelativeColumn(1.4f); // Package
                            columns.RelativeColumn(1.1f); // Event Date
                            columns.RelativeColumn(1f);   // Amount Paid
                            columns.RelativeColumn(1f);   // Balance
                            columns.RelativeColumn(1.1f); // Payment Status
                            columns.RelativeColumn(1.1f); // Refund Status
                        });

                        table.Header(header =>
                        {
                            void HeaderCell(string text) =>
                                header.Cell().Element(c => c.Background(Colors.Grey.Lighten2).Padding(4)).Text(text).Bold();

                            HeaderCell("Booking");
                            HeaderCell("Client");
                            HeaderCell("Event Type");
                            HeaderCell("Package");
                            HeaderCell("Event Date");
                            HeaderCell("Amount Paid");
                            HeaderCell("Balance");
                            HeaderCell("Payment");
                            HeaderCell("Refund");
                        });

                        foreach (var row in rows)
                        {
                            table.Cell().Padding(4).Text(row.BookingCode);
                            table.Cell().Padding(4).Text(row.ClientName);
                            table.Cell().Padding(4).Text(row.EventType);
                            table.Cell().Padding(4).Text(row.PackageName);
                            table.Cell().Padding(4).Text(row.EventDate.ToString("MMM dd, yyyy"));
                            table.Cell().Padding(4).Text($"₱{row.AmountPaid:N2}");
                            table.Cell().Padding(4).Text($"₱{row.RemainingBalance:N2}");
                            table.Cell().Padding(4).Text(row.PaymentStatus);
                            table.Cell().Padding(4).Text(row.RefundStatus);
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();
        }
    }
}
