namespace ParkWise.Application.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Attendant = "Attendant";
    public const string Driver = "Driver";

    public const string Staff = $"{Admin},{Attendant}";

    public static readonly IReadOnlyList<string> All = [Admin, Attendant, Driver];
}
