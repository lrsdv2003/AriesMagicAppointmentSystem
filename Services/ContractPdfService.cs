using AriesMagicAppointmentSystem.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AriesMagicAppointmentSystem.Services
{
    public class ContractPdfService
    {
        public byte[] GenerateContractPdf(Booking booking)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);

                    page.Content().Column(col =>
                    {
                        col.Item().AlignCenter().Text("Contract of Agreement")
                            .Bold().FontSize(16);

                        col.Item().PaddingTop(15).Text(
                            $"Contract of Agreement between {booking.Client?.FullName ?? "Client"} and Lazaro T. Benedicto III (Aries The Magic Artist Service Provider).");

                        col.Item().PaddingTop(10).Text(
                            $"This agreement is for the booking scheduled on {booking.EventDate:MMMM dd, yyyy} at {booking.StartTime:hh:mm tt}.");

                        col.Item().PaddingTop(10).Text($"Package: {booking.PackageName}")
                            .Bold();

                        col.Item().Text($"Package Rate: ₱{booking.FinalPrice:N2}");
                        col.Item().Text($"Required Downpayment: ₱{booking.RequiredDownpayment:N2}");
                        col.Item().Text($"Venue: {booking.PartyVenue}");
                        col.Item().Text($"Event Type: {booking.EventType}");
                        col.Item().Text($"Contact Person: {booking.ContactPerson}");
                        col.Item().Text($"Contact Number: {booking.ContactNumber}");

                        col.Item().PaddingTop(15).Text("Terms and Agreement").Bold();

                        col.Item().Text("1. A ₱2,000 downpayment is required to secure the reservation.");
                        col.Item().Text("2. The remaining balance will be paid after the event.");
                        col.Item().Text("3. Cancellation must be reported at least two weeks before the event.");
                        col.Item().Text("4. Rescheduling is subject to the availability of the service provider.");
                        col.Item().Text("5. Photos and videos will only be published after client approval.");

                        col.Item().PaddingTop(30).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("________________________");
                                c.Item().Text(booking.Client?.FullName ?? "Client");
                                c.Item().Text("Client");
                            });

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("________________________");
                                c.Item().Text("Lazaro T. Benedicto III");
                                c.Item().Text("Service Provider");
                            });
                        });
                    });
                });
            }).GeneratePdf();
        }
    }
}