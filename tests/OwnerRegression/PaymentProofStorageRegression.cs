using System.Reflection;
using System.Runtime.Loader;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

static class PaymentProofStorageRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool, string> check)
    {
        // This database belongs to the existing uniquely named LocalDB regression fixture.
        var root = Path.GetFullPath(Path.Combine(".tmp-owner-build", "payment-proof-fixture-" + Guid.NewGuid().ToString("N")));
        var environment = new ProofEnvironment(root);
        Directory.CreateDirectory(environment.WebRootPath);
        var privateRoot = Path.Combine(environment.ContentRootPath, "App_Data", "financial-proofs");
        var configuredBase = Path.Combine(root, "private-base");
        const string name = "original-fixture.png";
        byte[] original = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0];
        var defaultPath = Path.Combine(privateRoot, "payments", name);
        var configuredPath = Path.Combine(configuredBase, "financial-proofs", "payments", name);
        var legacyPath = Path.Combine(environment.WebRootPath, "uploads", "payments", name);
        foreach (var path in new[] { defaultPath, configuredPath, legacyPath })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, original);
        }

        PaymentsController Controller(string? basePath = null, string role = "Owner", string uid = "fixture-owner")
        {
            var settings = new Dictionary<string, string?>();
            if (basePath is not null) settings["PrivateStorage:RootPath"] = basePath;
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            return new(db, environment, null!, null!, null!, null!, null!, configuration, null!)
            {
                ControllerContext = new() { HttpContext = new DefaultHttpContext {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, uid)], "Fixture")) } }
            };
        }
        var serve = typeof(PaymentsController).GetMethod("ServeFinancialProof", BindingFlags.NonPublic | BindingFlags.Instance)!;
        IActionResult Serve(PaymentsController controller, string? path) => (IActionResult)serve.Invoke(controller, [path])!;
        bool Throws<T>(PaymentsController controller, string? path) where T : Exception
        {
            try { Serve(controller, path); return false; }
            catch (TargetInvocationException error) when (error.InnerException is T) { return true; }
        }
        void Physical(PaymentsController controller, string stored, string expected, string label)
        {
            var result = Serve(controller, stored) as PhysicalFileResult;
            check(result?.FileName == expected && result.ContentType == "image/png" && result.EnableRangeProcessing,
                "Proof resolver uses expected protected file: " + label);
            check(controller.Response.Headers.CacheControl.ToString().Contains("no-store"), "Proof response remains noncached: " + label);
        }
        Physical(Controller(), "payments/" + name, defaultPath, "default App_Data base");
        Physical(Controller(configuredBase), "payments/" + name, configuredPath, "configured base appends financial-proofs");
        Physical(Controller(), "/uploads/payments/" + name, legacyPath, "historical leading-slash uploads format");
        Physical(Controller(), "/UPLOADS/payments/" + name, Path.Combine(environment.WebRootPath, "UPLOADS", "payments", name), "case-insensitive legacy prefix on Windows");
        Physical(Controller(Path.Combine(environment.WebRootPath, "invalid-private-base")), "/uploads/payments/" + name,
            legacyPath, "legacy reads do not depend on configured private base");

        foreach (var path in new[] { "payments/missing.png", "/uploads/payments/missing.png", "uploads/payments/" + name,
                     "~/uploads/payments/" + name, "financial-proofs/payments/" + name, name })
            check(Serve(Controller(), path) is NotFoundResult, "Missing/mismapped proof reference yields 404: " + path);
        check(Serve(Controller(Path.Combine(root, "different-base")), "payments/" + name) is NotFoundResult,
            "Original file at former base does not silently fall back from a changed private base");
        check(Serve(Controller(Path.Combine(configuredBase, "financial-proofs")), "payments/" + name) is NotFoundResult,
            "Configuring the final financial-proofs directory double-appends it and produces missing-file 404");
        check(Throws<InvalidOperationException>(Controller(), ""), "Empty reference is a resolver exception, not an explicit 404");
        check(Throws<NullReferenceException>(Controller(), null), "Unexpected null bypassing model constraints is not an explicit 404");
        check(Throws<InvalidOperationException>(Controller(), "../../outside.png"), "Private traversal is rejected without serving a file");
        check(Serve(Controller(), "/uploads/../../outside.png") is ForbidResult, "Legacy traversal escaping web root is forbidden, not 404");
        check(Throws<InvalidOperationException>(Controller(environment.WebRootPath), "payments/" + name),
            "Private base beneath web root fails closed instead of serving publicly");
        var rootMethod = typeof(PaymentsController).GetMethod("PrivateProofRoot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        check((string)rootMethod.Invoke(Controller(""), null)! == Path.GetFullPath("financial-proofs"),
            "Empty configured base is not the null/default App_Data base");

        var booking = await db.Bookings.AsNoTracking().FirstAsync();
        var payment = new Payment { BookingId = booking.Id, Amount = 1, ProofImagePath = "payments/" + name, Status = PaymentStatus.Rejected };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        check(await Controller().PaymentProof(int.MaxValue) is NotFoundResult, "Absent payment ID returns 404 before resolving a file");
        check(await Controller().PaymentProof(payment.Id) is PhysicalFileResult,
            "Owner can read existing rejected payment proof; payment decision is not a 404 condition");
        check(await Controller(role: "Client", uid: "definitely-unrelated-" + Guid.NewGuid().ToString("N")).PaymentProof(payment.Id) is ForbidResult,
            "Unrelated Client receives Forbid, not a masked 404");
        check(await Controller(role: "Admin").PaymentProof(payment.Id) is ForbidResult, "Admin is not a financial proof reader");
        payment.ProofImagePath = "payments/missing.png";
        await db.SaveChangesAsync();
        var before = await db.Payments.AsNoTracking().Where(p => p.Id == payment.Id)
            .Select(p => new { p.ProofImagePath, p.Status, p.Amount, p.BookingId }).SingleAsync();
        check(await Controller().PaymentProof(payment.Id) is NotFoundResult,
            "Existing SQL payment row without its external image reproduces Owner proof 404");
        var after = await db.Payments.AsNoTracking().Where(p => p.Id == payment.Id)
            .Select(p => new { p.ProofImagePath, p.Status, p.Amount, p.BookingId }).SingleAsync();
        check(before.Equals(after), "Failed proof GET does not modify payment reference, status, amount or booking");
        var inventory = await File.ReadAllTextAsync("scripts/Inspect-PaymentProofReference.sql");
        await db.Database.OpenConnectionAsync();
        try
        {
            using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = inventory.Replace("DECLARE @PaymentId int = 3;", "DECLARE @PaymentId int = " + payment.Id + ";");
            using var reader = await command.ExecuteReaderAsync();
            check(await reader.ReadAsync() && reader.GetString(reader.GetOrdinal("ReferenceClass")).StartsWith("Private:", StringComparison.Ordinal),
                "Optional SELECT-only reference query classifies a synthetic private reference without outputting it");
        }
        finally { await db.Database.CloseConnectionAsync(); }
        foreach (var path in new[] { defaultPath, configuredPath, legacyPath })
            check((await File.ReadAllBytesAsync(path)).SequenceEqual(original), "Original synthetic proof bytes remain unchanged");

        foreach (var folder in new[] { "publish-net10", "publish-net10-production-hardened", "publish-net10-config-protected" })
        {
            var dll = Path.GetFullPath(Path.Combine(folder, "AriesMagicAppointmentSystem.dll"));
            if (!File.Exists(dll)) { Console.WriteLine("SKIP optional proof-package comparison: " + folder); continue; }
            var load = new AssemblyLoadContext("ProofFixture-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var type = load.LoadFromAssemblyPath(dll).GetType(typeof(PaymentsController).FullName!)!;
                var controller = type.GetConstructors().Single().Invoke([null, environment, null, null, null, null, null,
                    new ConfigurationBuilder().Build(), null]);
                ((Microsoft.AspNetCore.Mvc.Controller)controller).ControllerContext = new() { HttpContext = new DefaultHttpContext() };
                var method = type.GetMethod("ServeFinancialProof", BindingFlags.NonPublic | BindingFlags.Instance)!;
                check(method.Invoke(controller, ["payments/" + name]) is PhysicalFileResult privateResult && privateResult.FileName == defaultPath,
                    "Actual local package retains default private mapping: " + folder);
                check(method.Invoke(controller, ["/uploads/payments/" + name]) is PhysicalFileResult legacyResult && legacyResult.FileName == legacyPath,
                    "Actual local package retains historical legacy mapping: " + folder);
            }
            finally { load.Unload(); }
        }
    }

    sealed class ProofEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ProofStorageFixture";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = Path.Combine(root, "app");
        public string WebRootPath { get; set; } = Path.Combine(root, "app", "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
