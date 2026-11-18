using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Mvc.ModelBinding;

internal static class ProMaxEventDetailsTests
{
    private static int checkedCount;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception("FAILED: " + name);
        checkedCount++;
        Console.WriteLine("PASS: " + name);
    }

    public static void Run()
    {
        var exact = new[] {
            "Birthday", "Company Events", "Bridal Shower", "Gender Reveal",
            "School Events", "Halloween", "Christmas Parties", "Easter Events"
        };
        Check(BookingEventDetailsPolicy.EventTypes.SequenceEqual(exact),
            "Eight published event categories remain unchanged");
        Check(BookingEventDetailsPolicy.For("Wedding") == null,
            "Unsupported Wedding category is not invented");

        foreach (var type in exact)
        {
            var rule = BookingEventDetailsPolicy.For(type);
            Check(rule != null, type + " has a server-provided visibility policy");
            var modelState = new ModelStateDictionary();
            var result = BookingEventDetailsPolicy.ValidateAndNormalize(
                type, "  Silver ", "  Superheroes ", "  Client's child ", 8, modelState);
            Check(result.Motif == (rule!.ShowMotif ? "Silver" : ""),
                type + " motif relevance is normalized");
            Check(result.Theme == (rule.ShowTheme ? "Superheroes" : null),
                type + " theme relevance is normalized");
            Check(result.Celebrant == (rule.ShowCelebrant ? "Client's child" : null),
                type + " celebrant relevance is normalized");
            Check(result.Age == (rule.ShowAge ? 8 : null),
                type + " age relevance is normalized");
            Check(modelState.IsValid, type + " valid relevant details pass validation");
        }
        var gender = BookingEventDetailsPolicy.For("Gender Reveal")!;
        Check(gender.ShowMotif && gender.ShowTheme && !gender.ShowCelebrant && !gender.ShowAge,
            "Gender Reveal never asks for unrelated celebrant and age");
        var company = BookingEventDetailsPolicy.For("Company Events")!;
        Check(!company.ShowCelebrant && !company.ShowAge && !company.ShowTheme && !company.ShowMotif,
            "Company Events show only common information");
        var school = BookingEventDetailsPolicy.For("School Events")!;
        Check(!school.ShowCelebrant && !school.ShowAge, "School Events avoid celebrant details");

        var hiddenFieldModelState = new ModelStateDictionary();
        hiddenFieldModelState.AddModelError("Age","Bad stale age");
        hiddenFieldModelState.AddModelError("PartyTheme","Bad stale theme");
        hiddenFieldModelState.AddModelError("Motif","Bad stale motif");
        hiddenFieldModelState.AddModelError("CelebrantName","Bad stale celebrant");
        var normalized = BookingEventDetailsPolicy.ValidateAndNormalize("Company Events",
            "Overposted value", "Overposted theme", "Overposted name", 109, hiddenFieldModelState);
        Check(hiddenFieldModelState.IsValid && normalized is {Motif: "", Theme: null, Celebrant: null, Age: null},
            "Stale hidden fields cannot contaminate server-side booking validation or storage");

        var birthdayState=new ModelStateDictionary();
        BookingEventDetailsPolicy.ValidateAndNormalize("Birthday",
            null, null, null, null, birthdayState);
        Check(!birthdayState.IsValid && birthdayState.ContainsKey("CelebrantName"),
            "Birthday celebrant name remains required for business purposes");
        Check(!birthdayState.ContainsKey("Age") && !birthdayState.ContainsKey("PartyTheme")
            && !birthdayState.ContainsKey("Motif"),
            "Age, theme, and motif are optional rather than fabricated to satisfy the form");

        var bridalState = new ModelStateDictionary();
        BookingEventDetailsPolicy.ValidateAndNormalize("Bridal Shower",
            null, null, null, null, bridalState);
        Check(!bridalState.IsValid && bridalState.ContainsKey("CelebrantName"),
            "Bridal Shower requires only the previously established celebrant field");

        var unsupported=new ModelStateDictionary();
        BookingEventDetailsPolicy.ValidateAndNormalize("Made-up Event",
            null, null, null, null, unsupported);
        Check(!unsupported.IsValid && unsupported.ContainsKey("EventType"),
            "Unexpected event category is rejected by backend");

        Console.WriteLine($"PRO_MAX_EVENT_DETAILS_PASS checks={checkedCount}");
    }
}
