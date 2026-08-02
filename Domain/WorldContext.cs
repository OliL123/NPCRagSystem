namespace NPCRAGSystem.Domain;

public record WorldContext(
    int Hour,
    int Minute,
    int GameDay,         // absolute in-game day (1-based) — lets memories phrase recency relative to now
    string TimeLabel,    // "before dawn", "early morning", "morning", "midday", "afternoon", "early evening", "evening", "late night"
    string DayOfWeek,    // "Monday" … "Sunday"
    string Season,       // "spring", "summer", "autumn", "winter"
    string Weather       // e.g. "clear and mild", "overcast", "light rain"
)
{
    // Where the NPC currently is. The base builder knows only time/weather; the pipeline fills
    // these in from the current location. Empty when unknown. Lets an NPC ground "here"/"out"/
    // place-dependent talk instead of guessing.
    public string LocationName { get; init; } = "";
    public string LocationRegion { get; init; } = "";

    // 360-day year, 4 seasons × 90 days
    public static string SeasonFromDay(int gameDay)
    {
        var dayOfYear = ((gameDay - 1) % 360) + 1;
        return dayOfYear switch
        {
            <= 90  => "spring",
            <= 180 => "summer",
            <= 270 => "autumn",
            _      => "winter"
        };
    }

    // Canonical day-of-week index used everywhere (schedules + narration):
    // day 1 = Monday, 0=Monday … 6=Sunday. This must match NpcScheduleEntry's
    // "days" convention — schedules are authored as 0=Monday.
    public static int DayOfWeekIndex(int gameDay) => (((gameDay - 1) % 7) + 7) % 7;

    public static string DayNameFromDay(int gameDay) => DayOfWeekIndex(gameDay) switch
    {
        0 => "Monday",
        1 => "Tuesday",
        2 => "Wednesday",
        3 => "Thursday",
        4 => "Friday",
        5 => "Saturday",
        6 => "Sunday",
        _ => "unknown"
    };

    public static string TimeLabelFromHour(int hour) => hour switch
    {
        >= 0 and < 5   => "before dawn",
        >= 5 and < 7   => "early morning",
        >= 7 and < 11  => "morning",
        >= 11 and < 13 => "midday",
        >= 13 and < 17 => "afternoon",
        >= 17 and < 19 => "early evening",
        >= 19 and < 22 => "evening",
        _              => "late night"
    };

    // Deterministic weather from day + season — changes every 2 days
    public static string WeatherFromDay(int gameDay)
    {
        var season = SeasonFromDay(gameDay);
        var slot = (gameDay / 2) % 5;

        return season switch
        {
            "spring" => slot switch
            {
                0 => "mild and overcast",
                1 => "light rain",
                2 => "clear and breezy",
                3 => "warm and partly cloudy",
                _ => "bright, with patches of cloud"
            },
            "summer" => slot switch
            {
                0 => "hot and clear",
                1 => "warm and hazy",
                2 => "clear",
                3 => "hot, with brief afternoon cloud",
                _ => "dry and bright"
            },
            "autumn" => slot switch
            {
                0 => "cool and overcast",
                1 => "rainy",
                2 => "clear and crisp",
                3 => "foggy in the low streets",
                _ => "cold wind off the ravine"
            },
            "winter" => slot switch
            {
                0 => "cold and clear",
                1 => "grey and wet",
                2 => "frost on the stone",
                3 => "heavy cloud, no rain yet",
                _ => "cold, the sky the colour of old iron"
            },
            _ => "mild"
        };
    }
}
