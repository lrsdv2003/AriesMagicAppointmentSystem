namespace AriesMagicAppointmentSystem.ViewModels;

// Replace approved media here; keep descriptive alt text alongside each source.
public sealed record ClientMedia(string Key, string Source, string Alt, bool Placeholder = true)
{
    public static ClientMedia Get(string key) => key switch
    {
        "gallery-2" => new(key, "/images/client/rings.svg", "Illustrated interlocking rings on a burgundy stage"),
        "gallery-3" => new(key, "/images/client/curtain.svg", "Illustrated theatrical curtain and spotlight"),
        _ => new(key, "/images/client/stage.svg", "Illustrated stage with a rising sun and sweeping red curtains")
    };
}
