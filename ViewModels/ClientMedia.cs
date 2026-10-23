namespace AriesMagicAppointmentSystem.ViewModels;

// Replace approved media here; keep descriptive alt text alongside each source.
public sealed record ClientMedia(string Key, string Source, string Alt, bool Placeholder = true, int Width = 1200, int Height = 900)
{
    public static ClientMedia Get(string key) => key switch
    {
        "hero" or "gallery-1" => new(key, "/images/client/stage.svg", "Illustrated teal stage with coral curtains, a golden star, and floating magic cards", Width:960, Height:1200),
        "showcase" or "detail" => new(key, "/images/client/showcase.svg", "Illustrated performance stage with theatrical lights and a golden star", Width:1600, Height:900),
        "gallery-2" or "intro" or "service-2" => new(key, "/images/client/rings.svg", "Illustrated interlocking teal and ivory rings over coral and butter yellow"),
        _ => new(key, "/images/client/curtain.svg", "Illustrated coral curtains, spotlight, and a magic hat on a forest teal stage")
    };
}
