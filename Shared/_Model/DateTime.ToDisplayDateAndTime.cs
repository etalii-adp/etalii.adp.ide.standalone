namespace EtAlii.Adp;

public static class DateTimeToDisplayDateAndTime
{
    public static string ToDisplayDateAndTime(this DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
    public static string ToDisplayDateAndTime(this DateTime? dateTime)
    {
        return dateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";
    }

    public static string ToDisplayTime(this DateTime dateTime)
    {
        return dateTime.ToString("HH:mm:ss");
    }
    public static string ToDisplayTime(this DateTime? dateTime)
    {
        return dateTime?.ToString("HH:mm:ss") ?? "Never";
    }

    public static string ToDisplayDate(this DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd");
    }
    public static string ToDisplayDate(this DateTime? dateTime)
    {
        return dateTime?.ToString("yyyy-MM-dd") ?? "Never";
    }
}