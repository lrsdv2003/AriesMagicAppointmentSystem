using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class OwnerRound2Regression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var financials = new PaymentFinancialService(db);
        var controller = new PaymentsController(db, null!, null!, null!, null!, null!, null!, new ConfigurationBuilder().Build(), financials);
        var client = new User {FullName="Round 2 Queue Fixture", Role="Client"};
        var package = new Service {Name="Round 2 Queue Package", Price=10000};
        var timestamp = new DateTime(2026,11,15,1,0,0);
        var pending = new List<Payment>();
        var refunds = new List<RefundRequest>();
        for (var i=0;i<263;i++)
        {
            var booking = new Booking {Client=client, Service=package, PackageName=package.Name, CreatedAt=timestamp,
                EventDate=timestamp.AddMonths(1), StartTime=timestamp.AddMonths(1), EndTime=timestamp.AddMonths(1).AddHours(2), FinalPrice=10000,
                Status=i%3==0?BookingStatus.Cancelled:i%3==1?BookingStatus.Expired:BookingStatus.AwaitingVerification};
            pending.Add(new Payment {Booking=booking, Amount=2000, ExpectedAmountAtUpload=2000, UploadedAt=timestamp, PaymentMethod="GCash", Status=PaymentStatus.Pending});
            refunds.Add(new RefundRequest {Booking=booking, Amount=1000, RequestedAt=timestamp, RefundReferenceNumber="ROUND2-RETURN-"+i,
                Status=i%3==0?RefundStatus.Pending:i%3==1?RefundStatus.RefundProcessing:RefundStatus.Refunded});
        }
        db.Payments.AddRange(pending); db.RefundRequests.AddRange(refunds); await db.SaveChangesAsync();
        var expected = pending.Select(p=>p.Id).OrderDescending().ToArray();
        var paymentPage = (ViewResult)await controller.PendingVerification(q:client.FullName,page:2,pageSize:25);
        var paymentNavigation = (QueuePaginationViewModel)paymentPage.ViewData["QueuePage"]!;
        check(((IEnumerable<Payment>)paymentPage.Model!).Select(p=>p.Id).SequenceEqual(expected.Skip(25).Take(25)),"Payment SQL paging is stable for identical submission times");
        check(paymentNavigation.TotalCount==263 && paymentNavigation.AwaitingReviewCount==263 && paymentNavigation.Page==2 && paymentNavigation.TotalPages==11,"Payment counts cover all matching records, beyond the old 250-row cap");
        var last = (ViewResult)await controller.PendingVerification(q:client.FullName,page:int.MaxValue,pageSize:25);
        var lastNavigation = (QueuePaginationViewModel)last.ViewData["QueuePage"]!;
        check(lastNavigation.Page==11 && ((IEnumerable<Payment>)last.Model!).Count()==13 && lastNavigation.First==251 && lastNavigation.Last==263,"Old payment records after row 250 remain reachable and oversized pages clamp safely");
        var invalid = (ViewResult)await controller.PendingVerification(q:client.FullName,page:-10,pageSize:int.MaxValue);
        var invalidNavigation = (QueuePaginationViewModel)invalid.ViewData["QueuePage"]!;
        check(invalidNavigation.Page==1 && invalidNavigation.PageSize==10 && ((IEnumerable<Payment>)invalid.Model!).Count()==10,"Untrusted page and page-size inputs are bounded");
        var code=$"BK-{pending[0].Booking!.CreatedAt.Year}-{pending[0].BookingId:D3}";
        var codeSearch=(ViewResult)await controller.PendingVerification(q:code,paymentMethod:"GCash",date:timestamp.Date);
        check(((IEnumerable<Payment>)codeSearch.Model!).Single().Id==pending[0].Id,"Payment search finds full displayed booking code with combined method/date filters");
        check(((QueuePaginationViewModel)codeSearch.ViewData["QueuePage"]!).Routes()["q"]==code && ((QueuePaginationViewModel)codeSearch.ViewData["QueuePage"]!).Routes()["date"]=="2026-11-15","Payment refresh and paging preserve supported filters");
        var empty=(ViewResult)await controller.PendingVerification(q:"No matching Round2 record",page:999);
        var emptyNavigation=(QueuePaginationViewModel)empty.ViewData["QueuePage"]!;
        check(emptyNavigation.TotalCount==0 && emptyNavigation.Page==1 && emptyNavigation.First==0 && emptyNavigation.Last==0,"Empty payment results render valid zero ranges");
        var refundPage=(ViewResult)await controller.RefundRequests(q:client.FullName,page:11,pageSize:25);
        var refundNavigation=(QueuePaginationViewModel)refundPage.ViewData["QueuePage"]!;
        check(refundNavigation.TotalCount==263 && ((IEnumerable<RefundRequest>)refundPage.Model!).Count()==13,"Refund paging retains all records beyond 250, including archived bookings");
        check(refundNavigation.AwaitingReviewCount==88 && refundNavigation.AwaitingFinalVerificationCount==88,"Refund summary counts include every matching page");
        var refundSearch=(ViewResult)await controller.RefundRequests(q:code);
        check(((IEnumerable<RefundRequest>)refundSearch.Model!).Single().Id==refunds[0].Id,"Refund search accepts full displayed booking IDs");
        var referenceSearch=(ViewResult)await controller.RefundRequests(RefundStatus.Refunded,"ROUND2-RETURN-2");
        check(((IEnumerable<RefundRequest>)referenceSearch.Model!).All(r=>r.Status==RefundStatus.Refunded) && ((QueuePaginationViewModel)referenceSearch.ViewData["QueuePage"]!).TotalCount>0,"Refund completion-reference search composes with status filtering");
        var awaiting=(ViewResult)await controller.RefundRequests(RefundStatus.UnderReview,client.FullName,pageSize:50);
        check(((QueuePaginationViewModel)awaiting.ViewData["QueuePage"]!).TotalCount==88 && ((IEnumerable<RefundRequest>)awaiting.Model!).All(r=>r.Status==RefundStatus.Pending),"Awaiting Owner Review retains existing Pending/UnderReview grouping");
        check(await controller.RefundReview(refunds[0].Id) is ViewResult && refunds[0].Booking!.Status==BookingStatus.Cancelled,"Pending refund on cancelled booking remains accessible without changing booking status");
        check(await controller.RefundReview(refunds[1].Id) is ViewResult && refunds[1].Booking!.Status==BookingStatus.Expired,"Refund final verification on expired booking remains accessible");
        check(await db.Payments.CountAsync(p=>p.Booking!.ClientId==client.Id && p.Status==PaymentStatus.Pending)==263,"Queue GETs do not approve or mutate payments");
        check(ApplicationRoles.All.SequenceEqual(new[]{"Owner","Staff","Client"}),"Exactly three active roles remain after Owner refinements");

        // Direct calculator cases isolate the ledger rules from any dashboard presentation.
        var bookingForFinance=new Booking {FinalPrice=10000,RequiredDownpayment=2000,Status=BookingStatus.Confirmed};
        void Finance(string label,Payment[] payments,RefundRequest[] requests,decimal collected,decimal returned,decimal balance)
        {
            var f=financials.Calculate(bookingForFinance,payments,requests);
            check(f.TotalVerifiedPayments==collected && f.CompletedRefunds==returned && f.NetCollected==Math.Max(0,collected-returned) && f.RemainingBalance==balance,label);
        }
        Payment P(decimal amount,string status)=>new(){Amount=amount,Status=status};
        Finance("Unpaid booking has no collected revenue",[],[],0,0,10000);
        Finance("Unverified proof does not count as collected or reduce balance",[P(2000,PaymentStatus.Pending)],[],0,0,10000);
        Finance("Rejected proof does not count as collected or reduce balance",[P(2000,PaymentStatus.Rejected)],[],0,0,10000);
        Finance("Additional evidence remains uncollected",[P(2000,PaymentStatus.AdditionalEvidenceRequired)],[],0,0,10000);
        Finance("Partial verified payment reduces only its actual balance",[P(2000,PaymentStatus.Verified)],[],2000,0,8000);
        Finance("Fully verified payment settles booking balance",[P(10000,PaymentStatus.Verified)],[],10000,0,0);
        Finance("Pending refund does not remove retained collection",[P(2000,PaymentStatus.Verified)],[new(){Amount=2000,Status=RefundStatus.Pending}],2000,0,8000);
        Finance("Approved uncompleted refund is not counted as paid",[P(2000,PaymentStatus.Verified)],[new(){Amount=2000,Status=RefundStatus.Approved}],2000,0,8000);
        Finance("Completed refund uses the approved amount and reduces net collection",[P(2000,PaymentStatus.Verified)],[new(){Amount=2000,ApprovedAmount=1500,Status=RefundStatus.Refunded}],2000,1500,8000);
        Finance("Rejected additional proof preserves prior verified collection",[P(2000,PaymentStatus.Verified),P(8000,PaymentStatus.Rejected)],[],2000,0,8000);
    }
}
