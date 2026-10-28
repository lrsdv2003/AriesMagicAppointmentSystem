namespace AriesMagicAppointmentSystem.ViewModels;

// Curated, user-approved project photography only. Never resolve uploads or private storage here.
public sealed record GalleryPhoto(string Key, string Group, string Title, string Alt,
    string Original, int Width, int Height, string Sizes, string? Credit = null)
{
    public static IReadOnlyList<GalleryPhoto> Collection { get; } = Array.AsReadOnly(new[] {
        new GalleryPhoto("illusion", "performance", "The moment of surprise",
            "Aries performing an illusion with a young volunteer on a decorated birthday stage",
            "~/images/client/about/illusion.jpeg", 2048, 1365,
            "(max-width: 767px) calc(100vw - 48px), (max-width: 1279px) 64vw, 820px"),
        new GalleryPhoto("fire", "performance", "In the spotlight",
            "Aries in a red jacket holding a flaming wand above his head during a performance",
            "~/images/client/gallery/fire.jpeg", 1367, 2048,
            "(max-width: 767px) 70vw, (max-width: 1279px) 28vw, 365px", "Monarch / Rio Jones Photography"),
        new GalleryPhoto("audience", "connection", "A room watching together",
            "Children and adults watching Aries share a bird performance in a balloon-decorated venue",
            "~/images/client/wonder-studio/audience-wonder.jpeg", 2048, 1365,
            "(max-width: 767px) calc(100vw - 48px), (max-width: 1279px) 52vw, 660px"),
        new GalleryPhoto("participation", "connection", "The magic of taking part",
            "Aries holding a yellow and white snake as children and guests reach out to take part",
            "~/images/client/about/participation.jpeg", 2048, 1365,
            "(max-width: 767px) calc(100vw - 48px), (max-width: 1279px) 40vw, 510px"),
        new GalleryPhoto("silks", "connection", "A little colour, a shared moment",
            "Aries holding colourful silks beside a young volunteer in a blue racing costume",
            "~/images/client/gallery/silks.jpeg", 2048, 1365,
            "(max-width: 767px) calc(100vw - 48px), (max-width: 1279px) 40vw, 510px"),
        new GalleryPhoto("bear", "celebration", "Room for a bigger surprise",
            "Aries holding a large teddy bear above a group of children at a decorated celebration",
            "~/images/client/gallery/bear.jpeg", 1245, 1731,
            "(max-width: 767px) 70vw, (max-width: 1279px) 28vw, 365px"),
        new GalleryPhoto("celebration", "celebration", "Everyone in the picture",
            "Aries and fellow entertainers with children at a pink balloon-decorated celebration",
            "~/images/client/wonder-studio/celebration.jpeg", 1600, 1057,
            "(max-width: 767px) calc(100vw - 48px), (max-width: 1279px) 64vw, 820px")
    });
}
